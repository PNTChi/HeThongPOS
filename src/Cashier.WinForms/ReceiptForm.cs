using System.Text;
using Cashier.Core;

namespace Cashier.WinForms;

public sealed class ReceiptForm : Form
{
    private readonly Receipt _receipt;
    private readonly Label _status = Ui.Label("Giao dịch đã lưu. In lại không tạo thêm giao dịch.");
    public ReceiptForm(Receipt receipt)
    {
        _receipt = receipt;
        Ui.FormStyle(this, "Hóa đơn đã thanh toán", new Size(760, 750));
        MinimumSize = new Size(600, 540);
        var root = Ui.Rows(SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize, SizeType.AutoSize);
        root.Padding = new Padding(20);
        root.Controls.Add(Ui.Label("Hóa đơn đã ghi nhận", 18, true), 0, 0);
        var text = new ReceiptView();
        text.ShowReceipt(receipt);
        root.Controls.Add(text, 0, 1);
        Ui.WrapToParent(_status, root);
        root.Controls.Add(_status, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var print = Ui.Button("In hóa đơn", true);
        print.Width = 135;
        print.Click += (_, _) => Print(false);
        var preview = Ui.Button("Xem trước khi in");
        preview.Width = 165;
        preview.Click += (_, _) => Print(true);
        var export = Ui.Button("Lưu bản văn bản");
        export.Width = 155;
        export.Click += (_, _) => Export();
        var close = Ui.Button("Đóng");
        close.Width = 90;
        close.DialogResult = DialogResult.Cancel;
        actions.Controls.AddRange([print, preview, export, close]);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);
        CancelButton = close;
    }
    private void Print(bool preview)
    {
        try
        {
            using var printer = new ReceiptPrinter(_receipt);
            if (preview) printer.Preview(this); else printer.PrintReceipt(this);
            _status.Text = "Hóa đơn vẫn được giữ nguyên. Có thể gửi lệnh in lại nếu cần.";
        }
        catch (Exception e)
        {
            _status.Text = "Không in được: " + e.Message + " Hóa đơn đã lưu; kiểm tra máy in và thử lại.";
            Ui.Error(this, new IOException(_status.Text, e));
        }
    }
    private void Export()
    {
        try
        {
            using var save = new SaveFileDialog { Filter = "Hóa đơn văn bản UTF-8 (*.txt)|*.txt", FileName = _receipt.ReceiptNumber + ".txt", OverwritePrompt = true };
            if (save.ShowDialog(this) == DialogResult.OK)
            {
                File.WriteAllText(save.FileName, ReceiptFormatter.Format(_receipt), new UTF8Encoding(true));
                _status.Text = "Đã lưu bản văn bản của hóa đơn. Giao dịch không thay đổi.";
            }
        }
        catch (Exception e) { Ui.Error(this, e); }
    }
}
