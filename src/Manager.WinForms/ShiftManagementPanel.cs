using System.Collections.Immutable;
using Cashier.Core;
using Manager.Core;

namespace Manager.WinForms;

public sealed class ShiftManagementPanel : UserControl
{
    private readonly ManagerController _controller;

    // Shift status labels
    private readonly Label _lblCurrentShift;
    private readonly Label _lblShiftStaff;
    private readonly Label _lblShiftTime;
    private readonly Label _lblShiftStatus;

    // Method breakdown grid
    private readonly DataGridView _gridBreakdown;

    // Reconciliation controls
    private readonly Label _lblStartingCash;
    private readonly Label _lblExpectedCash;
    private readonly NumericUpDown _numActualCash;
    private readonly Label _lblDifference;
    private readonly TextBox _txtNotes;

    // Action buttons
    private readonly Button _btnOpenShift;
    private readonly Button _btnCloseShift;
    private readonly Button _btnRefresh;
    private readonly Button _btnViewTransactions;

    // Open shift dialog inputs
    private readonly TextBox _txtNewShiftId;
    private readonly NumericUpDown _numNewStartingCash;
    private readonly Panel _openShiftPanel;
    private readonly Panel _activeShiftPanel;

    // Shift history grid
    private readonly DataGridView _gridHistory;

    private ShiftRecord? _currentShift;

    public event Action<string>? OnViewShiftTransactionsRequested;

    public ShiftManagementPanel(ManagerController controller)
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
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));  // Header status cards
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));   // Current Shift / Reconciliation
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));   // Shift History

        // 1. Header status bar (matching top of Figure 2.23)
        var headerCard = ManagerUi.CardPanel();
        headerCard.Dock = DockStyle.Fill;
        var headerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        _lblCurrentShift = ManagerUi.Label("Ca hiện tại: ---", 10.5f, true, ManagerUi.Primary);
        _lblShiftStaff = ManagerUi.Label("Nhân viên: ---", 10f);
        _lblShiftTime = ManagerUi.Label("Thời gian: ---", 10f);
        _lblShiftStatus = ManagerUi.Label("Trạng thái: Chưa mở ca", 10.5f, true, ManagerUi.Danger);

        headerLayout.Controls.Add(_lblCurrentShift, 0, 0);
        headerLayout.Controls.Add(_lblShiftStaff, 1, 0);
        headerLayout.Controls.Add(_lblShiftTime, 2, 0);
        headerLayout.Controls.Add(_lblShiftStatus, 3, 0);
        headerCard.Controls.Add(headerLayout);

        // 2. Main Work Area: Active Shift Reconciliation & Open Shift
        var workPanel = new Panel { Dock = DockStyle.Fill };

        // 2a. Open Shift Panel (shown when no shift is open)
        _openShiftPanel = ManagerUi.CardPanel();
        _openShiftPanel.Dock = DockStyle.Fill;
        var openLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16) };
        openLayout.Controls.Add(ManagerUi.Label("KHỞI TẠO VÀ BẮT ĐẦU CA LÀM VIỆC MỚI", 12f, bold: true, ManagerUi.Primary));
        openLayout.Controls.Add(ManagerUi.Label("Chưa có ca làm việc nào đang mở trên thiết bị. Vui lòng mở ca trước khi giao dịch bán hàng.", 9.5f));

        var openForm = new TableLayoutPanel { Width = 600, Height = 100, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 12, 0, 12) };
        openForm.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        openForm.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _txtNewShiftId = new TextBox { Dock = DockStyle.Fill };
        _numNewStartingCash = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100000000, Increment = 50000, Value = 1000000, ThousandsSeparator = true };

        openForm.Controls.Add(ManagerUi.FormLabel("Mã ca làm việc:"), 0, 0);
        openForm.Controls.Add(_txtNewShiftId, 1, 0);
        openForm.Controls.Add(ManagerUi.FormLabel("Tiền mặt đầu ca (đ):"), 0, 1);
        openForm.Controls.Add(_numNewStartingCash, 1, 1);
        openLayout.Controls.Add(openForm);

        _btnOpenShift = ManagerUi.Button("Mở ca làm việc", primary: true, height: 40);
        _btnOpenShift.Click += async (s, e) => await OpenShiftAsync();
        openLayout.Controls.Add(_btnOpenShift);
        _openShiftPanel.Controls.Add(openLayout);

        // 2b. Active Shift Panel (matching Figure 2.23)
        _activeShiftPanel = new Panel { Dock = DockStyle.Fill };
        var activeLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        activeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); // Breakdown grid
        activeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45)); // Reconciliation inputs

        // Breakdown grid (Figure 2.23: Phương thức | Số giao dịch | Số tiền | Đối chiếu)
        _gridBreakdown = ManagerUi.CreateDataGrid();
        _gridBreakdown.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Phương thức", DataPropertyName = "Method", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _gridBreakdown.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Số giao dịch", DataPropertyName = "Count", Width = 110 });
        _gridBreakdown.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Số tiền", DataPropertyName = "AmountDisplay", Width = 130 });
        _gridBreakdown.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Đối chiếu", DataPropertyName = "Note", Width = 140 });

        var breakdownCard = ManagerUi.CardPanel();
        breakdownCard.Dock = DockStyle.Fill;
        var breakdownWrap = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        breakdownWrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        breakdownWrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        breakdownWrap.Controls.Add(ManagerUi.Label("DOANH THU THEO PHƯƠNG THỨC THANH TOÁN TRONG CA", 10f, true, ManagerUi.Primary), 0, 0);
        breakdownWrap.Controls.Add(_gridBreakdown, 0, 1);
        breakdownCard.Controls.Add(breakdownWrap);

        // Reconciliation card
        var reconCard = ManagerUi.CardPanel();
        reconCard.Name = "ShiftReconciliationCard";
        reconCard.Dock = DockStyle.Fill;
        reconCard.AutoScroll = true;
        var reconLayout = new TableLayoutPanel
        {
            Name = "ShiftReconciliationLayout", Dock = DockStyle.Top, ColumnCount = 2, RowCount = 7,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty
        };
        reconLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        reconLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 7; row++) reconLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _lblStartingCash = ManagerUi.Label("0 đ", 10f, true);
        _lblExpectedCash = ManagerUi.Label("0 đ", 10f, true, ManagerUi.Primary);
        _lblExpectedCash.Name = "ExpectedCash";
        _numActualCash = new NumericUpDown
        {
            Name = "ActualCash", Dock = DockStyle.Fill, Minimum = 0, Maximum = 1000000000,
            Increment = 50000, ThousandsSeparator = true, Margin = new Padding(3, 4, 3, 8)
        };
        _numActualCash.ValueChanged += (s, e) => CalculateDifference();

        _lblDifference = ManagerUi.Label("0 đ", 10.5f, true, ManagerUi.Success);
        _lblDifference.Name = "CashDifference";
        _txtNotes = new TextBox
        {
            Name = "ReconciliationNotes", Dock = DockStyle.Fill, Multiline = true, Height = 54,
            MinimumSize = new Size(0, 54), ScrollBars = ScrollBars.Vertical
        };

        reconLayout.Controls.Add(ManagerUi.Label("Tiền mặt đầu ca:", 9.5f, true), 0, 0);
        reconLayout.Controls.Add(_lblStartingCash, 1, 0);

        reconLayout.Controls.Add(ManagerUi.Label("Tiền mặt dự kiến:", 9.5f, true), 0, 1);
        reconLayout.Controls.Add(_lblExpectedCash, 1, 1);

        reconLayout.Controls.Add(ManagerUi.Label("Tiền mặt thực tế:", 9.5f, true), 0, 2);
        reconLayout.Controls.Add(_numActualCash, 1, 2);

        reconLayout.Controls.Add(ManagerUi.Label("Chênh lệch:", 9.5f, true), 0, 3);
        reconLayout.Controls.Add(_lblDifference, 1, 3);

        reconLayout.Controls.Add(ManagerUi.Label("Ghi chú đối soát:", 9.5f), 0, 4);
        reconLayout.Controls.Add(_txtNotes, 1, 4);

        var actions = new FlowLayoutPanel
        {
            Name = "ShiftReconciliationActions", Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 8, 0, 0)
        };
        _btnRefresh = ManagerUi.Button("Làm mới số liệu");
        _btnRefresh.Name = "RefreshShift";
        _btnRefresh.Click += async (s, e) => await RefreshDataAsync();
        actions.Controls.Add(_btnRefresh);

        _btnViewTransactions = ManagerUi.Button("Xem giao dịch");
        _btnViewTransactions.Name = "ViewShiftTransactions";
        _btnViewTransactions.Click += (s, e) =>
        {
            if (_currentShift != null) OnViewShiftTransactionsRequested?.Invoke(_currentShift.ShiftId);
        };
        actions.Controls.Add(_btnViewTransactions);

        _btnCloseShift = ManagerUi.Button("Xác nhận đóng ca", primary: true);
        _btnCloseShift.Name = "CloseShift";
        _btnCloseShift.Click += async (s, e) => await CloseShiftAsync();
        actions.Controls.Add(_btnCloseShift);

        reconLayout.Controls.Add(actions, 0, 6);
        reconLayout.SetColumnSpan(actions, 2);

        var hintLbl = ManagerUi.Label(
            "Tiền mặt dự kiến = tiền mặt đầu ca + doanh thu tiền mặt (không gồm chuyển khoản).\n" +
            "Chênh lệch = tiền mặt thực tế − tiền mặt dự kiến. Nhập số tiền đã kiểm đếm trước khi đóng ca.",
            9f, false, ManagerUi.Muted);
        hintLbl.Name = "ReconciliationFormula";
        hintLbl.Margin = new Padding(3, 10, 3, 4);
        reconLayout.Controls.Add(hintLbl, 0, 5);
        reconLayout.SetColumnSpan(hintLbl, 2);

        reconCard.Controls.Add(reconLayout);
        // Constrain the wrapping controls to the visible card width. Auto-sized rows can then
        // grow when buttons wrap, and the card scrolls if a small window cannot fit the content.
        void UpdateReconciliationWidth()
        {
            var width = Math.Max(1, reconCard.ClientSize.Width - reconCard.Padding.Horizontal -
                (reconCard.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
            reconLayout.MaximumSize = new Size(width, 0);
            actions.MaximumSize = new Size(Math.Max(1, width - actions.Margin.Horizontal), 0);
            hintLbl.MaximumSize = new Size(Math.Max(1, width - hintLbl.Margin.Horizontal), 0);
        }
        reconCard.ClientSizeChanged += (_, _) => UpdateReconciliationWidth();
        reconCard.Layout += (_, _) => UpdateReconciliationWidth();

        activeLayout.Controls.Add(breakdownCard, 0, 0);
        activeLayout.Controls.Add(reconCard, 1, 0);
        _activeShiftPanel.Controls.Add(activeLayout);

        workPanel.Controls.Add(_openShiftPanel);
        workPanel.Controls.Add(_activeShiftPanel);

        // 3. Bottom: Shift History
        var historyCard = ManagerUi.CardPanel();
        historyCard.Dock = DockStyle.Fill;
        var historyWrap = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        historyWrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        historyWrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        historyWrap.Controls.Add(ManagerUi.Label("LỊCH SỬ CÁC CA LÀM VIỆC ĐÃ ĐÓNG VÀ ĐỐI SOÁT", 10f, true, ManagerUi.Primary), 0, 0);

        _gridHistory = ManagerUi.CreateDataGrid();
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Mã ca", DataPropertyName = "ShiftId", Width = 130 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Nhân viên", DataPropertyName = "StaffName", Width = 150 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Bắt đầu", DataPropertyName = "OpenedAtDisplay", Width = 140 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Kết thúc", DataPropertyName = "ClosedAtDisplay", Width = 140 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Đầu ca", DataPropertyName = "StartingCashDisplay", Width = 110 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Thực tế", DataPropertyName = "ActualCashDisplay", Width = 110 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Chênh lệch", DataPropertyName = "DiffDisplay", Width = 110 });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Ghi chú", DataPropertyName = "Notes", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        historyWrap.Controls.Add(_gridHistory, 0, 1);
        historyCard.Controls.Add(historyWrap);

        mainLayout.Controls.Add(headerCard, 0, 0);
        mainLayout.Controls.Add(workPanel, 0, 1);
        mainLayout.Controls.Add(historyCard, 0, 2);

        Controls.Add(mainLayout);
    }

    private decimal _currentTheoreticalExpectedCash = 0;

    public async Task RefreshDataAsync()
    {
        try
        {
            _currentShift = await _controller.GetCurrentOpenShiftAsync();
            var history = await _controller.GetShiftHistoryAsync();

            if (_currentShift is not null && _currentShift.Status == ShiftStatus.Open)
            {
                _openShiftPanel.Visible = false;
                _activeShiftPanel.Visible = true;

                _lblCurrentShift.Text = $"Ca hiện tại: {_currentShift.ShiftId}";
                _lblShiftStaff.Text = $"Nhân viên: {_currentShift.StaffName}";
                _lblShiftTime.Text = $"Bắt đầu: {_currentShift.OpenedAt:dd/MM/yyyy HH:mm}";
                _lblShiftStatus.Text = "Trạng thái: Đang hoạt động";
                _lblShiftStatus.ForeColor = ManagerUi.Success;

                var recon = await _controller.PreviewShiftReconciliationAsync(
                    _numActualCash.Value,
                    _txtNotes.Text);

                _gridBreakdown.DataSource = new[]
                {
                    new { Method = "Tiền mặt (Cash)", Count = recon.CashOrdersCount, AmountDisplay = ManagerUi.FormatVnd(recon.CashTotal), Note = "Đã thu tại quầy" },
                    new { Method = "Chuyển khoản (Bank)", Count = recon.TransferOrdersCount, AmountDisplay = ManagerUi.FormatVnd(recon.TransferTotal), Note = "Đã nhận chuyển khoản" },
                    new { Method = "TỔNG CỘNG", Count = recon.TotalOrders, AmountDisplay = ManagerUi.FormatVnd(recon.TotalRevenue), Note = "Doanh thu ca" }
                };

                _lblStartingCash.Text = ManagerUi.FormatVnd(_currentShift.StartingCash);
                _currentTheoreticalExpectedCash = recon.ExpectedCash;
                _lblExpectedCash.Text = ManagerUi.FormatVnd(recon.ExpectedCash);

                CalculateDifference();
            }
            else
            {
                _openShiftPanel.Visible = true;
                _activeShiftPanel.Visible = false;

                _lblCurrentShift.Text = "Ca hiện tại: ---";
                _lblShiftStaff.Text = "Nhân viên: " + (_controller.CurrentUser?.FullName ?? "---");
                _lblShiftTime.Text = "Thời gian: ---";
                _lblShiftStatus.Text = "Trạng thái: Chưa mở ca";
                _lblShiftStatus.ForeColor = ManagerUi.Danger;

                _txtNewShiftId.Text = "CA-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmm");
                _numNewStartingCash.Value = 1000000;
            }

            // Populate history
            var closedShifts = history.Where(x => x.Status == ShiftStatus.Closed)
                .OrderByDescending(x => x.OpenedAt)
                .Select(x => new
                {
                    x.ShiftId,
                    x.StaffName,
                    OpenedAtDisplay = x.OpenedAt.ToString("dd/MM/yy HH:mm"),
                    ClosedAtDisplay = x.ClosedAt?.ToString("dd/MM/yy HH:mm") ?? "---",
                    StartingCashDisplay = ManagerUi.FormatVnd(x.StartingCash),
                    ActualCashDisplay = ManagerUi.FormatVnd(x.ActualCash ?? 0),
                    DiffDisplay = ManagerUi.FormatVnd(x.Difference),
                    Notes = x.Notes ?? ""
                }).ToList();

            _gridHistory.DataSource = closedShifts;
        }
        catch (Exception ex)
        {
            ManagerUi.Error(this, ex);
        }
    }

    private void CalculateDifference()
    {
        var actual = _numActualCash.Value;
        var diff = actual - _currentTheoreticalExpectedCash;
        if (diff == 0)
        {
            _lblDifference.Text = "0 đ (Khớp tiền 100%)";
            _lblDifference.ForeColor = ManagerUi.Success;
        }
        else if (diff > 0)
        {
            _lblDifference.Text = $"+{diff:N0} đ (Dư tiền)";
            _lblDifference.ForeColor = ManagerUi.Accent;
        }
        else
        {
            _lblDifference.Text = $"{diff:N0} đ (Thiếu hụt)";
            _lblDifference.ForeColor = ManagerUi.Danger;
        }
    }

    private async Task OpenShiftAsync()
    {
        try
        {
            var shiftId = _txtNewShiftId.Text.Trim();
            var starting = _numNewStartingCash.Value;
            var created = await _controller.OpenShiftAsync(shiftId, starting);

            ManagerUi.Info(this, $"Đã mở ca làm việc '{created.ShiftId}' thành công!");
            await RefreshDataAsync();
        }
        catch (Exception ex)
        {
            ManagerUi.Error(this, ex);
        }
    }

    private async Task CloseShiftAsync()
    {
        if (_currentShift == null) return;
        var actual = _numActualCash.Value;
        var diff = actual - _currentTheoreticalExpectedCash;

        string msg = $"Xác nhận đóng ca '{_currentShift.ShiftId}'?\n\n" +
                     $"• Tiền mặt dự kiến: {ManagerUi.FormatVnd(_currentTheoreticalExpectedCash)}\n" +
                     $"• Tiền mặt thực tế: {ManagerUi.FormatVnd(actual)}\n" +
                     $"• Chênh lệch: {ManagerUi.FormatVnd(diff)}";

        if (diff != 0)
            msg += "\n\n⚠️ Chú ý: Đang có chênh lệch tiền mặt! Bạn đã kiểm đếm kỹ chưa?";

        var confirm = MessageBox.Show(this, msg, "Xác nhận đóng ca & đối soát", MessageBoxButtons.YesNo,
            diff == 0 ? MessageBoxIcon.Question : MessageBoxIcon.Warning);

        if (confirm == DialogResult.Yes)
        {
            try
            {
                var closed = await _controller.CloseShiftAsync(actual, _txtNotes.Text);
                ManagerUi.Info(this, $"Đã đóng ca '{closed.ShiftId}' và lưu kết quả đối soát thành công!");
                _txtNotes.Text = "";
                _numActualCash.Value = 0;
                await RefreshDataAsync();
            }
            catch (Exception ex)
            {
                ManagerUi.Error(this, ex);
            }
        }
    }
}

