using Cashier.Core;
using Manager.Core;

namespace Manager.WinForms;

public sealed class ReportStatisticsPanel : UserControl
{
    private readonly ManagerController _controller;

    // Filter controls
    private readonly ComboBox _cbReportType;
    private readonly DateTimePicker _dtpFrom;
    private readonly DateTimePicker _dtpTo;
    private readonly TextBox _txtShift;
    private readonly Button _btnViewReport;
    private readonly Button _btnReset;

    // KPI Cards
    private readonly Label _lblKpiRevenue;
    private readonly Label _lblKpiOrders;
    private readonly Label _lblKpiCash;
    private readonly Label _lblKpiTransfer;
    private readonly Label _lblKpiDiscount;

    // Data Grid (Figure 2.25: Chỉ tiêu | Số lượng | Giá trị | Khoảng thống kê)
    private readonly DataGridView _gridMetrics;
    private readonly Label _lblEmpty;

    public ReportStatisticsPanel(ManagerController controller)
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
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));  // Filter
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));  // KPI cards
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Metrics Grid & Result

        // 1. Filter bar (Figure 2.25: Loại báo cáo | Khoảng thời gian | Ca làm việc)
        var filterCard = ManagerUi.CardPanel();
        filterCard.Dock = DockStyle.Fill;
        var filterLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };

        filterLayout.Controls.Add(ManagerUi.Label("Loại báo cáo:", 9.5f, true));
        _cbReportType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
        _cbReportType.Items.AddRange([
            "1. Doanh thu theo thời gian",
            "2. Doanh thu theo ca làm việc",
            "3. Top sản phẩm bán chạy",
            "4. Phân bổ phương thức thanh toán"
        ]);
        _cbReportType.SelectedIndex = 0;
        filterLayout.Controls.Add(_cbReportType);

        filterLayout.Controls.Add(ManagerUi.Label("Từ:", 9.5f, true));
        _dtpFrom = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110, Value = DateTime.Today.AddDays(-7) };
        filterLayout.Controls.Add(_dtpFrom);

        filterLayout.Controls.Add(ManagerUi.Label("Đến:", 9.5f, true));
        _dtpTo = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110, Value = DateTime.Today.AddDays(1) };
        filterLayout.Controls.Add(_dtpTo);

        filterLayout.Controls.Add(ManagerUi.Label("Ca:", 9.5f, true));
        _txtShift = new TextBox { Width = 100 };
        filterLayout.Controls.Add(_txtShift);

        _btnViewReport = ManagerUi.Button("Xem báo cáo", primary: true, height: 32);
        _btnViewReport.Click += async (s, e) => await LoadReportAsync();
        filterLayout.Controls.Add(_btnViewReport);

        _btnReset = ManagerUi.Button("Đặt lại bộ lọc", height: 32);
        _btnReset.Click += async (s, e) => await ResetFiltersAsync();
        filterLayout.Controls.Add(_btnReset);

        filterCard.Controls.Add(filterLayout);

        // 2. KPI Cards
        var kpiPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1 };
        kpiPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        kpiPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        kpiPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        kpiPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        kpiPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

        _lblKpiRevenue = CreateKpiCard(kpiPanel, 0, "TỔNG DOANH THU", "0 đ", ManagerUi.Primary);
        _lblKpiOrders = CreateKpiCard(kpiPanel, 1, "TỔNG SỐ ĐƠN HÀNG", "0", ManagerUi.Ink);
        _lblKpiCash = CreateKpiCard(kpiPanel, 2, "DOANH THU TIỀN MẶT", "0 đ", ManagerUi.Success);
        _lblKpiTransfer = CreateKpiCard(kpiPanel, 3, "DOANH THU CHUYỂN KHOẢN", "0 đ", ManagerUi.Accent);
        _lblKpiDiscount = CreateKpiCard(kpiPanel, 4, "TỔNG TIỀN GIẢM GIÁ", "0 đ", ManagerUi.Muted);

        // 3. Metrics Grid (Figure 2.25: Chỉ tiêu | Số lượng | Giá trị | Khoảng thống kê)
        var gridCard = ManagerUi.CardPanel();
        gridCard.Dock = DockStyle.Fill;
        var gridLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        gridLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        gridLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titleLbl = ManagerUi.Label("CHI TIẾT CHỈ TIÊU BÁO CÁO", 10.5f, true, ManagerUi.Primary);
        gridLayout.Controls.Add(titleLbl, 0, 0);

        _gridMetrics = ManagerUi.CreateDataGrid();
        _gridMetrics.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Chỉ tiêu", DataPropertyName = "Indicator", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _gridMetrics.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Số lượng", DataPropertyName = "QuantityDisplay", Width = 130 });
        _gridMetrics.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Giá trị", DataPropertyName = "ValueDisplay", Width = 180 });
        _gridMetrics.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Khoảng thống kê / Tỷ lệ", DataPropertyName = "Period", Width = 220 });

        _lblEmpty = new Label
        {
            Text = "Không có giao dịch nào trong khoảng thời gian đã chọn.",
            ForeColor = ManagerUi.Muted,
            Font = new Font("Segoe UI", 11, FontStyle.Italic),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Visible = false
        };

        var wrapper = new Panel { Dock = DockStyle.Fill };
        wrapper.Controls.Add(_lblEmpty);
        wrapper.Controls.Add(_gridMetrics);
        gridLayout.Controls.Add(wrapper, 0, 1);

        gridCard.Controls.Add(gridLayout);

        mainLayout.Controls.Add(filterCard, 0, 0);
        mainLayout.Controls.Add(kpiPanel, 0, 1);
        mainLayout.Controls.Add(gridCard, 0, 2);

        Controls.Add(mainLayout);
    }

    private static Label CreateKpiCard(TableLayoutPanel parent, int col, string title, string defaultValue, Color valColor)
    {
        var card = ManagerUi.CardPanel(new Padding(12, 8, 12, 8));
        card.Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0), Padding = new Padding(0) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titleLbl = new Label
        {
            Text = title,
            ForeColor = ManagerUi.Muted,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0, 0, 0, 2)
        };
        var valLbl = new Label
        {
            Text = defaultValue,
            ForeColor = valColor,
            Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 2, 0, 0),
            AutoEllipsis = true
        };

        layout.Controls.Add(titleLbl, 0, 0);
        layout.Controls.Add(valLbl, 0, 1);
        card.Controls.Add(layout);
        parent.Controls.Add(card, col, 0);
        return valLbl;
    }

    public async Task RefreshDataAsync() => await LoadReportAsync();

    private async Task ResetFiltersAsync()
    {
        _cbReportType.SelectedIndex = 0;
        _dtpFrom.Value = DateTime.Today.AddDays(-7);
        _dtpTo.Value = DateTime.Today.AddDays(1);
        _txtShift.Text = "";
        await LoadReportAsync();
    }

    private async Task LoadReportAsync()
    {
        try
        {
            var kind = _cbReportType.SelectedIndex switch
            {
                1 => ReportKind.RevenueByShift,
                2 => ReportKind.TopProducts,
                3 => ReportKind.PaymentBreakdown,
                _ => ReportKind.RevenueByPeriod
            };

            var from = new DateTimeOffset(_dtpFrom.Value.Date);
            var to = new DateTimeOffset(_dtpTo.Value.Date.AddDays(1).AddTicks(-1));
            var shiftId = string.IsNullOrWhiteSpace(_txtShift.Text) ? null : _txtShift.Text.Trim();

            var summary = await _controller.GenerateReportAsync(kind, from, to, shiftId);

            _lblKpiRevenue.Text = ManagerUi.FormatVnd(summary.TotalRevenue);
            _lblKpiOrders.Text = summary.TotalOrders.ToString();
            _lblKpiCash.Text = ManagerUi.FormatVnd(summary.CashTotal);
            _lblKpiTransfer.Text = ManagerUi.FormatVnd(summary.TransferTotal);
            _lblKpiDiscount.Text = ManagerUi.FormatVnd(summary.TotalDiscount);

            if (summary.TotalOrders == 0)
            {
                _lblEmpty.Visible = true;
                _gridMetrics.Visible = false;
            }
            else
            {
                _lblEmpty.Visible = false;
                _gridMetrics.Visible = true;

                var rows = summary.Metrics.Select(m => new
                {
                    m.Indicator,
                    QuantityDisplay = m.Quantity.ToString("N0"),
                    ValueDisplay = ManagerUi.FormatVnd(m.Value),
                    m.Period
                }).ToList();

                _gridMetrics.DataSource = rows;
            }
        }
        catch (Exception ex)
        {
            ManagerUi.Error(this, ex);
        }
    }
}

