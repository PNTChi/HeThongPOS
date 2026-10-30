using Cashier.Core;

namespace Cashier.WinForms;

// Presentation only: all amounts come from the persisted receipt.
internal sealed class ReceiptLayout : IDisposable
{
    internal sealed record Row(string Left = "", string Right = "", bool Bold = false,
        bool Center = false, bool Rule = false, bool Title = false, float Gap = 0);
    internal sealed record Line(Row Row, float Height);
    private readonly Font _normal = new("Segoe UI", 10);
    private readonly Font _bold = new("Segoe UI", 10, FontStyle.Bold);
    private readonly Font _title = new("Segoe UI", 14, FontStyle.Bold);
    private Font FontFor(Row row) => row.Title ? _title : row.Bold ? _bold : _normal;

    internal static IEnumerable<Row> Rows(Receipt receipt)
    {
        CartRules.ValidateReceipt(receipt);
        yield return new("POS NHÓM 11", Center: true);
        yield return new("HÓA ĐƠN BÁN HÀNG", Center: true, Title: true, Gap: 10);
        if (receipt.IsDemo) yield return new("DỮ LIỆU MẪU — KHÔNG PHẢI GIAO DỊCH THỰC TẾ", Center: true, Gap: 10);
        yield return new($"Mã: {receipt.ReceiptNumber}");
        yield return new($"Ngày: {receipt.PaidAt.ToLocalTime():dd/MM/yyyy HH:mm:ss}");
        yield return new($"Thu ngân: {receipt.StaffName}");
        yield return new($"Ca: {receipt.ShiftId}");
        yield return new(Rule: true);
        foreach (var item in receipt.Lines)
        {
            yield return new(item.ProductName, Bold: true);
            if (item.Options.Length > 0) yield return new(item.OptionText);
            yield return new($"{item.Quantity} × {CartRules.Money(item.UnitPrice)}", CartRules.Money(item.LineTotal), Gap: 12);
        }
        yield return new(Rule: true);
        yield return new("Tạm tính", CartRules.Money(receipt.Totals.Subtotal), Gap: 4);
        yield return new("Giảm giá" + (receipt.Promotion is null ? "" : " (" + receipt.Promotion.Code + ")"), CartRules.Money(receipt.Totals.Discount), Gap: 8);
        yield return new("TỔNG THANH TOÁN", CartRules.Money(receipt.Totals.Payable), Bold: true, Gap: 4);
        yield return new(Rule: true);
        yield return new($"Phương thức: {(receipt.Payment.Method == PaymentMethod.Cash ? "Tiền mặt" : "Chuyển khoản — thu ngân xác nhận")}", Gap: 4);
        if (receipt.Payment.Method == PaymentMethod.Cash)
        {
            yield return new("Khách đưa", CartRules.Money(receipt.Payment.AmountReceived), Gap: 4);
            yield return new("Tiền thừa", CartRules.Money(receipt.Payment.Change));
        }
        yield return new(Rule: true);
        yield return new("Giao dịch đã ghi nhận. Cảm ơn quý khách!", Center: true);
    }

    internal List<Line> Measure(Graphics g, IEnumerable<Row> rows, float width)
    {
        var result = new List<Line>();
        foreach (var row in rows)
        {
            if (row.Rule) { result.Add(new(row, 22)); continue; }
            var font = FontFor(row);
            var rightWidth = row.Right.Length == 0 ? 0 : g.MeasureString(row.Right, font).Width + 16;
            // Stack unusually wide values on narrow receipt paper rather than overlap.
            var stack = rightWidth > width * .55f;
            var parts = Wrap(g, row.Left, font, Math.Max(1, width - (stack ? 0 : rightWidth))).ToArray();
            for (var i = 0; i < parts.Length; i++)
                result.Add(new(row with { Left = parts[i], Right = !stack && i == parts.Length - 1 ? row.Right : "", Gap = 0 },
                    font.GetHeight(g) * 1.35f));
            if (stack)
                foreach (var part in Wrap(g, row.Right, font, Math.Max(1, width)))
                    result.Add(new(row with { Left = "", Right = part, Gap = 0 }, font.GetHeight(g) * 1.35f));
            if (row.Gap > 0) result.Add(new(new Row(), row.Gap));
        }
        return result;
    }

    private static IEnumerable<string> Wrap(Graphics g, string text, Font font, float width)
    {
        if (text.Length == 0) { yield return ""; yield break; }
        while (text.Length > 0)
        {
            var take = text.Length;
            while (take > 1 && g.MeasureString(text[..take], font).Width > width) take--;
            if (take < text.Length)
            {
                var space = text.LastIndexOf(' ', take - 1, take);
                if (space > 0) take = space;
            }
            yield return text[..take];
            text = text[take..].TrimStart();
        }
    }

    internal void Draw(Graphics g, Line line, float x, float y, float width)
    {
        var row = line.Row;
        if (row.Rule)
        {
            using var pen = new Pen(Color.LightGray);
            g.DrawLine(pen, x, y + 10, x + width, y + 10);
            return;
        }
        var font = FontFor(row);
        using var format = new StringFormat { Alignment = row.Center ? StringAlignment.Center : StringAlignment.Near };
        g.DrawString(row.Left, font, Brushes.Black, new RectangleF(x, y, width, line.Height), format);
        if (row.Right.Length > 0)
        {
            format.Alignment = StringAlignment.Far;
            g.DrawString(row.Right, font, Brushes.Black, new RectangleF(x, y, width, line.Height), format);
        }
    }

    public void Dispose() { _normal.Dispose(); _bold.Dispose(); _title.Dispose(); }
}

internal sealed class ReceiptView : Panel
{
    private readonly ReceiptLayout _layout = new();
    private List<ReceiptLayout.Line> _lines = [];
    private Receipt? _receipt;
    private float _width;
    public ReceiptView()
    {
        Dock = DockStyle.Fill;
        AutoScroll = true;
        BackColor = Color.White;
        DoubleBuffered = true;
        AccessibleRole = AccessibleRole.Document;
        Resize += (_, _) => Reflow();
    }
    internal void ShowReceipt(Receipt? receipt)
    {
        _receipt = receipt;
        AccessibleName = receipt is null ? "Chưa có hóa đơn phù hợp. Giỏ chưa thanh toán không nằm trong lịch sử." : ReceiptFormatter.Format(receipt);
        AutoScrollPosition = Point.Empty;
        Reflow();
    }
    private void Reflow()
    {
        if (ClientSize.Width < 1) return;
        var scale = DeviceDpi / 96f;
        _width = Math.Max(1, (ClientSize.Width - SystemInformation.VerticalScrollBarWidth) / scale - 40);
        // Fonts use points; use a stable 96-DPI surface for layout and screen drawing.
        using var bitmap = new Bitmap(1, 1);
        bitmap.SetResolution(96, 96);
        using var measure = Graphics.FromImage(bitmap);
        _lines = _layout.Measure(measure, _receipt is null ? [new ReceiptLayout.Row(AccessibleName ?? "")] : ReceiptLayout.Rows(_receipt), _width);
        AutoScrollMinSize = new Size(0, (int)Math.Ceiling((_lines.Sum(x => x.Height) + 40) * scale));
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var scale = DeviceDpi / 96f;
        // Render at 96 DPI, then scale once for high-DPI displays.
        using var bitmap = new Bitmap(Math.Max(1, (int)Math.Ceiling(_width + 40)), Math.Max(1, (int)Math.Ceiling(ClientSize.Height / scale)));
        bitmap.SetResolution(96, 96);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.White);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var y = 20 + AutoScrollPosition.Y / scale;
            foreach (var line in _lines)
            {
                if (y + line.Height >= 0 && y < bitmap.Height) _layout.Draw(g, line, 20, y, _width);
                y += line.Height;
            }
        }
        e.Graphics.DrawImage(bitmap, new Rectangle(0, 0, (int)Math.Ceiling(bitmap.Width * scale), ClientSize.Height));
    }
    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) _layout.Dispose();
        base.Dispose(disposing);
    }
}
