using Cashier.Core;

namespace Cashier.WinForms;

public sealed class PaymentForm : Form
{
    private readonly CashierController _controller;
    private readonly ComboBox _method = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown _cash = new()
    {
        Minimum = 0, Maximum = decimal.MaxValue, DecimalPlaces = 0, ThousandsSeparator = true, Increment = 1000, Dock = DockStyle.Fill
    };
    private readonly CheckBox _transfer = new() { Text = "Tôi đã kiểm tra và nhận đủ tiền chuyển khoản", AutoSize = true };
    private readonly Label _change = Ui.Label();
    private readonly Label _status = Ui.Label();
    private readonly Button _confirm = Ui.Button("Xác nhận thanh toán", true);
    private readonly Button _verify = Ui.Button("Kiểm tra giao dịch");
    private readonly Button _cancel = Ui.Button("Đóng");
    private bool _busy;
    public Receipt? CompletedReceipt { get; private set; }

    public PaymentForm(CashierController controller)
    {
        _controller = controller;
        Ui.FormStyle(this, "Thanh toán", new Size(650, 550));
        MinimumSize = new Size(600, 530);
        MaximizeBox = false;
        var root = Ui.Rows(SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize,
            SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize);
        root.Padding = new Padding(24);
        root.Controls.Add(Ui.Label("Thanh toán đơn hàng", 19, true), 0, 0);
        root.Controls.Add(Ui.Label("Cần thanh toán: " + CartRules.Money(controller.Totals.Payable), 20, true), 0, 1);
        var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 18, 0, 10) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.Controls.Add(Ui.Label("Phương thức:"), 0, 0);
        fields.Controls.Add(_method, 1, 0);
        var cashLabel = Ui.Label("Tiền khách đưa:");
        fields.Controls.Add(cashLabel, 0, 1);
        fields.Controls.Add(_cash, 1, 1);
        root.Controls.Add(fields, 0, 2);
        root.Controls.Add(_change, 0, 3);
        root.Controls.Add(_transfer, 0, 4);
        Ui.WrapToParent(_status, root);
        Ui.WrapToParent(_change, root);
        root.SizeChanged += (_, _) => _transfer.MaximumSize = new Size(Math.Max(1,
            root.ClientSize.Width - root.Padding.Horizontal - _transfer.Margin.Horizontal), 0);
        _status.ForeColor = Ui.Muted;
        root.Controls.Add(_status, 0, 5);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        _confirm.Width = 275;
        _verify.Width = 170;
        _cancel.Width = 90;
        actions.Controls.AddRange([_confirm, _verify, _cancel]);
        root.Controls.Add(actions, 0, 6);
        Controls.Add(root);
        _method.Items.AddRange(["Tiền mặt", "Chuyển khoản"]);
        _method.SelectedIndex = controller.PendingReceipt?.Payment.Method == PaymentMethod.BankTransfer ? 1 : 0;
        _cash.Value = Math.Min(_cash.Maximum, controller.PendingReceipt?.Payment.AmountReceived ?? controller.Totals.Payable);
        _transfer.Checked = controller.PendingReceipt?.Payment.Method == PaymentMethod.BankTransfer;
        _method.SelectedIndexChanged += (_, _) => RefreshState();
        _cash.ValueChanged += (_, _) => RefreshState();
        _transfer.CheckedChanged += (_, _) => RefreshState();
        _confirm.Click += async (_, _) => await ExecuteAsync(false);
        _verify.Click += async (_, _) => await ExecuteAsync(true);
        _cancel.Click += (_, _) => Close();
        _cancel.DialogResult = DialogResult.Cancel;
        CancelButton = _cancel;
        _method.SelectedIndexChanged += (_, _) =>
        {
            cashLabel.Visible = _cash.Visible = _method.SelectedIndex == 0;
        };
        cashLabel.Visible = _cash.Visible = _method.SelectedIndex == 0;
        FormClosing += (_, e) => { if (_busy) e.Cancel = true; };
        _status.Text = controller.PendingReceipt is null ?
            "Chuyển khoản do thu ngân xác nhận thủ công; ứng dụng không tự kết nối ngân hàng." : controller.Notice;
        RefreshState();
    }

    private void RefreshState()
    {
        var pending = _controller.PendingReceipt is not null;
        _method.Enabled = !_busy && !pending;
        _cash.Enabled = !_busy && !pending && _method.SelectedIndex == 0;
        _transfer.Enabled = !_busy && !pending && _method.SelectedIndex == 1;
        _transfer.Visible = _method.SelectedIndex == 1;
        var cashShort = _method.SelectedIndex == 0 && _cash.Value < _controller.Totals.Payable;
        _change.Text = _method.SelectedIndex == 0
            ? (cashShort
                ? "Tiền khách đưa chưa đủ — còn thiếu: " + CartRules.Money(_controller.Totals.Payable - _cash.Value)
                : "Tiền thừa: " + CartRules.Money(_cash.Value - _controller.Totals.Payable))
            : "Chuyển khoản: cần xác nhận đã nhận đủ " + CartRules.Money(_controller.Totals.Payable);
        _change.ForeColor = cashShort ? Ui.Warning : Ui.Ink;
        _confirm.Text = pending ? "Ghi lại cùng giao dịch" : "Xác nhận thanh toán";
        _confirm.Enabled = !_busy && _controller.State != CheckoutState.Uncertain &&
            (pending || (_method.SelectedIndex == 0 ? _cash.Value >= _controller.Totals.Payable : _transfer.Checked));
        _verify.Visible = pending;
        _verify.Enabled = !_busy && pending;
        _cancel.Enabled = !_busy;
    }

    private async Task ExecuteAsync(bool verify)
    {
        _busy = true;
        _status.Text = verify ? "Đang kiểm tra kết quả giao dịch…" : "Đang ghi nhận thanh toán…";
        RefreshState();
        try
        {
            var request = new PaymentRequest(_method.SelectedIndex == 0 ? PaymentMethod.Cash : PaymentMethod.BankTransfer,
                _cash.Value, _transfer.Checked);
            var result = verify ? await _controller.ResolvePendingAsync() : await _controller.Checkout(request);
            _status.Text = result.Message;
            if (result.Kind == CheckoutResultKind.Paid)
            {
                CompletedReceipt = result.Receipt;
                DialogResult = DialogResult.OK;
                _busy = false;
                Close();
            }
            else if (result.Kind == CheckoutResultKind.Rejected)
            {
                MessageBox.Show(this, result.Message, "Kiểm tra thanh toán", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _busy = false;
                Close(); // Reopen from the current cart/total, e.g. an expired promotion changed the payable.
            }
        }
        catch (Exception e) { _status.Text = e.Message; }
        finally { _busy = false; RefreshState(); }
    }
}
