using System.Drawing.Printing;
using Cashier.Core;

namespace Cashier.WinForms;

// Printing is independent of Checkout. A printer failure cannot create another order.
public sealed class ReceiptPrinter : IDisposable
{
    private readonly Receipt _receipt;
    private readonly PrintDocument _document = new();
    private readonly ReceiptLayout _layout = new();
    private List<ReceiptLayout.Line> _lines = [];
    private int _position;
    public ReceiptPrinter(Receipt receipt)
    {
        CartRules.ValidateReceipt(receipt);
        _receipt = receipt;
        _document.DocumentName = receipt.ReceiptNumber;
        _document.DefaultPageSettings.Margins = new Margins(35, 35, 40, 40);
        _document.BeginPrint += (_, _) => { _position = 0; _lines = []; };
        _document.PrintPage += OnPrintPage;
    }
    public void PrintReceipt(IWin32Window owner)
    {
        using var dialog = new PrintDialog { Document = _document, UseEXDialog = true, AllowSomePages = false };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;
        _document.Print();
        MessageBox.Show(owner, "Đã gửi lệnh in. Kiểm tra giấy và máy in; ứng dụng không xác nhận giấy đã in ra.", "In hóa đơn");
    }
    public void Preview(IWin32Window owner)
    {
        using var preview = new PrintPreviewDialog { Document = _document, Width = 1000, Height = 780, UseAntiAlias = true };
        preview.ShowDialog(owner);
    }
    private void OnPrintPage(object? sender, PrintPageEventArgs e)
    {
        if (e.Graphics is null) throw new InvalidOperationException("Không tạo được trang in.");
        if (_lines.Count == 0) _lines = _layout.Measure(e.Graphics, ReceiptLayout.Rows(_receipt), e.MarginBounds.Width);
        var y = (float)e.MarginBounds.Top;
        while (_position < _lines.Count && y + _lines[_position].Height <= e.MarginBounds.Bottom)
        {
            var line = _lines[_position++];
            _layout.Draw(e.Graphics, line, e.MarginBounds.Left, y, e.MarginBounds.Width);
            y += line.Height;
        }
        if (y == e.MarginBounds.Top && _position < _lines.Count)
            throw new InvalidOperationException("Khổ giấy/vùng in quá nhỏ; kiểm tra thiết lập máy in.");
        e.HasMorePages = _position < _lines.Count;
    }
    public void Dispose() { _document.Dispose(); _layout.Dispose(); }
}
