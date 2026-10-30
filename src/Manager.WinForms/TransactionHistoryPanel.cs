using System.Collections.Immutable;
using Cashier.Core;
using Manager.Core;

namespace Manager.WinForms;

public sealed class TransactionHistoryPanel : UserControl
{
    private readonly ManagerController _controller;

    // Filters
    private readonly DateTimePicker _dtpFrom;
    private readonly DateTimePicker _dtpTo;
    private readonly TextBox _txtStaffOrShift;
    private readonly ComboBox _cbMethod;
    private readonly TextBox _txtKeyword;
    private readonly Button _btnSearch;
    private readonly Button _btnReset;

    // Master Grid
    private readonly DataGridView _gridTransactions;
    private readonly Label _lblEmptyResult;

    // Detail Grid & Info
    private readonly Label _lblDetailHeader;
    private readonly DataGridView _gridLines;
    private readonly Label _lblSubtotal;
    private readonly Label _lblDiscount;
    private readonly Label _lblTotal;
    private readonly Label _lblPaymentInfo;

    private ImmutableArray<Receipt> _currentResults = [];

    public TransactionHistoryPanel(ManagerController controller)
    {
        _controller = controller;
        Dock = DockStyle.Fill;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = ManagerUi.Background;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));  // Filter bar
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));   // Master Grid
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));   // Detail Card

        // 1. Filter bar (matching Figure 2.24)
        var filterCard = ManagerUi.CardPanel(new Padding(12, 8, 12, 8));
        filterCard.Dock = DockStyle.Fill;
        var filterWrap = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2, Margin = new Padding(0), Padding = new Padding(0) };
        filterWrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        filterWrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        filterWrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        filterWrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        filterWrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

        filterWrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        filterWrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        _dtpFrom = new DateTimePicker { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Short, Value = DateTime.Today.AddDays(-7) };
        _dtpTo = new DateTimePicker { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Short, Value = DateTime.Today.AddDays(1) };
        _txtStaffOrShift = new TextBox { Dock = DockStyle.Fill };
        _cbMethod = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbMethod.Items.AddRange(["Tất cả phương thức", "Tiền mặt (Cash)", "Chuyển khoản (Bank)"]);
        _cbMethod.SelectedIndex = 0;

        _txtKeyword = new TextBox { Dock = DockStyle.Fill };

        filterWrap.Controls.Add(ManagerUi.Label("Từ ngày:", 9f, true), 0, 0);
        filterWrap.Controls.Add(_dtpFrom, 0, 1);

        filterWrap.Controls.Add(ManagerUi.Label("Đến ngày:", 9f, true), 1, 0);
        filterWrap.Controls.Add(_dtpTo, 1, 1);

        filterWrap.Controls.Add(ManagerUi.Label("Nhân viên / Ca làm việc:", 9f, true), 2, 0);
        filterWrap.Controls.Add(_txtStaffOrShift, 2, 1);

        filterWrap.Controls.Add(ManagerUi.Label("Phương thức thanh toán:", 9f, true), 3, 0);
        filterWrap.Controls.Add(_cbMethod, 3, 1);

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _btnSearch = ManagerUi.Button("Tra cứu", primary: true, height: 32);
        _btnSearch.Click += async (s, e) => await DoSearchAsync();
        btnPanel.Controls.Add(_btnSearch);

        _btnReset = ManagerUi.Button("Đặt lại", height: 32);
        _btnReset.Click += async (s, e) => await ResetFiltersAsync();
        btnPanel.Controls.Add(_btnReset);

        filterWrap.Controls.Add(ManagerUi.Label("Tìm theo mã/món:", 9f, true), 4, 0);
        filterWrap.Controls.Add(btnPanel, 4, 1);

        filterCard.Controls.Add(filterWrap);

        // 2. Master Grid
        var gridCard = ManagerUi.CardPanel();
        gridCard.Dock = DockStyle.Fill;

        _gridTransactions = ManagerUi.CreateDataGrid();
        _gridTransactions.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Mã hóa đơn", DataPropertyName = "ReceiptNumber", Width = 150 });
        _gridTransactions.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Thời điểm", DataPropertyName = "TimeDisplay", Width = 150 });
        _gridTransactions.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Nhân viên", DataPropertyName = "StaffName", Width = 150 });
        _gridTransactions.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Ca làm việc", DataPropertyName = "ShiftId", Width = 130 });
        _gridTransactions.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Phương thức", DataPropertyName = "MethodDisplay", Width = 130 });
        _gridTransactions.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tổng tiền", DataPropertyName = "TotalDisplay", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _gridTransactions.SelectionChanged += (s, e) => OnTransactionSelected();

        _lblEmptyResult = new Label
        {
            Text = "Không tìm thấy giao dịch nào phù hợp với điều kiện tra cứu.",
            ForeColor = ManagerUi.Muted,
            Font = new Font("Segoe UI", 11, FontStyle.Italic),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Visible = false
        };

        var masterWrap = new Panel { Dock = DockStyle.Fill };
        masterWrap.Controls.Add(_lblEmptyResult);
        masterWrap.Controls.Add(_gridTransactions);
        gridCard.Controls.Add(masterWrap);

        // 3. Detail Card (matching bottom of Figure 2.24)
        var detailCard = ManagerUi.CardPanel();
        detailCard.Dock = DockStyle.Fill;
        var detailLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        _lblDetailHeader = ManagerUi.Label("CHI TIẾT ĐƠN HÀNG: Chưa chọn giao dịch", 10.5f, true, ManagerUi.Primary);
        detailLayout.Controls.Add(_lblDetailHeader, 0, 0);

        _gridLines = ManagerUi.CreateDataGrid();
        _gridLines.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tên món", DataPropertyName = "ProductName", Width = 200 });
        _gridLines.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tùy chọn (Modifiers)", DataPropertyName = "OptionText", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _gridLines.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "SL", DataPropertyName = "Quantity", Width = 60 });
        _gridLines.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Đơn giá", DataPropertyName = "UnitPriceDisplay", Width = 110 });
        _gridLines.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Thành tiền", DataPropertyName = "LineTotalDisplay", Width = 130 });
        detailLayout.Controls.Add(_gridLines, 0, 1);

        var footerPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        _lblSubtotal = ManagerUi.Label("Tạm tính: 0 đ", 9.5f);
        _lblDiscount = ManagerUi.Label("Giảm giá: 0 đ", 9.5f);
        _lblTotal = ManagerUi.Label("Tổng thanh toán: 0 đ", 10f, true, ManagerUi.Accent);
        _lblPaymentInfo = ManagerUi.Label("Tiền khách đưa: 0 đ", 9.5f);

        footerPanel.Controls.Add(_lblSubtotal, 0, 0);
        footerPanel.Controls.Add(_lblDiscount, 1, 0);
        footerPanel.Controls.Add(_lblTotal, 2, 0);
        footerPanel.Controls.Add(_lblPaymentInfo, 3, 0);
        detailLayout.Controls.Add(footerPanel, 0, 2);

        detailCard.Controls.Add(detailLayout);

        mainLayout.Controls.Add(filterCard, 0, 0);
        mainLayout.Controls.Add(gridCard, 0, 1);
        mainLayout.Controls.Add(detailCard, 0, 2);

        Controls.Add(mainLayout);
    }

    public async Task RefreshDataAsync() => await DoSearchAsync();

    public void FilterByShift(string shiftId)
    {
        _txtStaffOrShift.Text = shiftId;
        _ = DoSearchAsync();
    }

    private async Task DoSearchAsync()
    {
        try
        {
            PaymentMethod? method = _cbMethod.SelectedIndex switch
            {
                1 => PaymentMethod.Cash,
                2 => PaymentMethod.BankTransfer,
                _ => null
            };

            var from = new DateTimeOffset(_dtpFrom.Value.Date);
            var to = new DateTimeOffset(_dtpTo.Value.Date.AddDays(1).AddTicks(-1));

            var filter = new HistoryFilter(
                FromDate: from,
                ToDate: to,
                StaffQuery: _txtStaffOrShift.Text,
                ShiftQuery: null,
                Method: method,
                Keyword: _txtKeyword.Text);

            _currentResults = await _controller.QueryTransactionsAsync(filter);

            if (_currentResults.Length == 0)
            {
                _lblEmptyResult.Visible = true;
                _gridTransactions.Visible = false;
                _gridLines.DataSource = null;
                _lblDetailHeader.Text = "CHI TIẾT ĐƠN HÀNG: Không có giao dịch";
                _lblSubtotal.Text = "Tạm tính: 0 đ";
                _lblDiscount.Text = "Giảm giá: 0 đ";
                _lblTotal.Text = "Tổng thanh toán: 0 đ";
                _lblPaymentInfo.Text = "Tiền khách đưa: 0 đ";
            }
            else
            {
                _lblEmptyResult.Visible = false;
                _gridTransactions.Visible = true;

                var rows = _currentResults.Select(r => new
                {
                    r.ReceiptNumber,
                    TimeDisplay = r.PaidAt.ToString("dd/MM/yyyy HH:mm"),
                    r.StaffName,
                    r.ShiftId,
                    MethodDisplay = r.Payment.Method == PaymentMethod.Cash ? "Tiền mặt" : "Chuyển khoản",
                    TotalDisplay = ManagerUi.FormatVnd(r.Totals.Payable),
                    Source = r
                }).ToList();

                _gridTransactions.DataSource = rows;
                if (rows.Count > 0)
                {
                    _gridTransactions.Rows[0].Selected = true;
                    OnTransactionSelected();
                }
            }
        }
        catch (Exception ex)
        {
            ManagerUi.Error(this, ex);
        }
    }

    private async Task ResetFiltersAsync()
    {
        _dtpFrom.Value = DateTime.Today.AddDays(-7);
        _dtpTo.Value = DateTime.Today.AddDays(1);
        _txtStaffOrShift.Text = "";
        _cbMethod.SelectedIndex = 0;
        _txtKeyword.Text = "";
        await DoSearchAsync();
    }

    private void OnTransactionSelected()
    {
        if (_gridTransactions.CurrentRow?.DataBoundItem != null)
        {
            dynamic item = _gridTransactions.CurrentRow.DataBoundItem;
            Receipt receipt = item.Source;

            _lblDetailHeader.Text = $"CHI TIẾT HÓA ĐƠN: {receipt.ReceiptNumber} | Lúc: {receipt.PaidAt:dd/MM/yyyy HH:mm:ss} | Thu ngân: {receipt.StaffName} ({receipt.StaffId}) | Ca: {receipt.ShiftId}";

            var lineRows = receipt.Lines.Select(l => new
            {
                l.ProductName,
                OptionText = string.IsNullOrWhiteSpace(l.OptionText) ? "---" : l.OptionText,
                l.Quantity,
                UnitPriceDisplay = ManagerUi.FormatVnd(l.UnitPrice),
                LineTotalDisplay = ManagerUi.FormatVnd(l.LineTotal)
            }).ToList();

            _gridLines.DataSource = lineRows;

            _lblSubtotal.Text = $"Tạm tính: {ManagerUi.FormatVnd(receipt.Totals.Subtotal)}";
            var discountNote = receipt.Promotion != null ? $" (-{receipt.Promotion.Code})" : "";
            _lblDiscount.Text = $"Giảm giá: {ManagerUi.FormatVnd(receipt.Totals.Discount)}{discountNote}";
            _lblTotal.Text = $"Cần thanh toán: {ManagerUi.FormatVnd(receipt.Totals.Payable)}";

            if (receipt.Payment.Method == PaymentMethod.Cash)
            {
                _lblPaymentInfo.Text = $"Tiền nhận: {ManagerUi.FormatVnd(receipt.Payment.AmountReceived)} | Tiền thừa: {ManagerUi.FormatVnd(receipt.Payment.Change)}";
            }
            else
            {
                _lblPaymentInfo.Text = "Phương thức: Chuyển khoản (Đã xác nhận)";
            }
        }
    }
}

