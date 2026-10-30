using Cashier.Core;
using Cashier.Infrastructure;
using Cashier.WinForms;
using Manager.Core;
using Manager.Infrastructure;

namespace Manager.WinForms;

public sealed class MainManagerForm : Form
{
    private readonly ManagerController _controller;
    private readonly string _catalogPath;
    private readonly string _receiptsDir;
    private readonly string _localDataDir;
    private readonly IOrderRepository _orderRepository;
    private readonly IReceiptHistory _receiptHistory;

    private readonly Label _lblUser;
    private readonly Label _lblShift;
    private readonly Button _btnPos;
    private readonly Button _btnLogout;

    private readonly TabControl _tabs;
    private readonly ProductManagementPanel _pnlProducts;
    private readonly CategoryManagementPanel _pnlCategories;
    private readonly ModifierManagementPanel _pnlModifiers;
    private readonly PromotionManagementPanel _pnlPromotions;
    private readonly StaffManagementPanel _pnlStaff;
    private readonly ShiftManagementPanel _pnlShifts;
    private readonly TransactionHistoryPanel _pnlHistory;
    private readonly ReportStatisticsPanel _pnlReports;

    public MainManagerForm(ManagerController controller, string catalogPath, string receiptsDir, string localDataDir,
        IOrderRepository orderRepository, IReceiptHistory receiptHistory)
    {
        _controller = controller;
        _catalogPath = catalogPath;
        _receiptsDir = receiptsDir;
        _localDataDir = localDataDir;
        _orderRepository = orderRepository;
        _receiptHistory = receiptHistory;

        Text = "HỆ THỐNG QUẢN LÝ POS (Quản lý: Nguyễn Ngọc Yến Nhi)";
        Font = new Font("Segoe UI", 9.5f);
        BackColor = ManagerUi.Background;
        ForeColor = ManagerUi.Ink;
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1100, 700);
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74)); // Top bar
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Tab content

        // Top bar
        var topBar = new Panel { Dock = DockStyle.Fill, BackColor = ManagerUi.HeaderBg, Padding = new Padding(16, 6, 16, 6) };
        var topLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        topLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titleLabel = new Label
        {
            Text = "☕ POS MANAGEMENT SYSTEM",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        };

        var infoPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 4, 0, 4)
        };
        _lblUser = new Label
        {
            Text = "👤 Người dùng: " + (_controller.CurrentUser?.FullName ?? "---"),
            ForeColor = Color.FromArgb(226, 232, 240),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 3)
        };
        _lblShift = new Label
        {
            Text = "⏱️ Ca: Đang kiểm tra...",
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("Segoe UI", 9f),
            AutoSize = true,
            Margin = new Padding(0, 1, 0, 0)
        };
        infoPanel.Controls.Add(_lblUser);
        infoPanel.Controls.Add(_lblShift);

        var topButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 10, 0, 0)
        };
        _btnLogout = ManagerUi.Button("Đăng xuất", height: 34);
        _btnLogout.Click += (s, e) => Logout();
        topButtons.Controls.Add(_btnLogout);

        _btnPos = ManagerUi.Button("Mở Bán Hàng (POS)", primary: true, height: 34);
        _btnPos.Click += async (s, e) => await OpenCashierPosAsync();
        topButtons.Controls.Add(_btnPos);

        topLayout.Controls.Add(titleLabel, 0, 0);
        topLayout.Controls.Add(infoPanel, 1, 0);
        topLayout.Controls.Add(topButtons, 2, 0);
        topBar.Controls.Add(topLayout);

        // Tab Control
        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Regular),
            Padding = new Point(16, 8)
        };

        _pnlProducts = new ProductManagementPanel(_controller);
        _pnlCategories = new CategoryManagementPanel(_controller);
        _pnlModifiers = new ModifierManagementPanel(_controller);
        _pnlPromotions = new PromotionManagementPanel(_controller);
        _pnlStaff = new StaffManagementPanel(_controller);
        _pnlShifts = new ShiftManagementPanel(_controller);
        _pnlHistory = new TransactionHistoryPanel(_controller);
        _pnlReports = new ReportStatisticsPanel(_controller);

        _pnlShifts.OnViewShiftTransactionsRequested += (shiftId) =>
        {
            _tabs.SelectedIndex = 6; // Go to History tab
            _pnlHistory.FilterByShift(shiftId);
        };

        AddTab("1. Sản phẩm", _pnlProducts);
        AddTab("2. Quản lý danh mục", _pnlCategories);
        AddTab("3. Modifier & Tùy chọn", _pnlModifiers);
        AddTab("4. Khuyến mãi", _pnlPromotions);
        AddTab("5. Nhân viên & Tài khoản", _pnlStaff);
        AddTab("6. Ca làm việc & Đối soát", _pnlShifts);
        AddTab("7. Lịch sử giao dịch", _pnlHistory);
        AddTab("8. Báo cáo & Thống kê", _pnlReports);

        _tabs.SelectedIndexChanged += async (s, e) => await OnTabChangedAsync();

        root.Controls.Add(topBar, 0, 0);
        root.Controls.Add(_tabs, 0, 1);
        Controls.Add(root);

        Load += async (s, e) =>
        {
            await UpdateTopShiftStatusAsync();
            await _pnlProducts.RefreshDataAsync();
        };
    }

    private void AddTab(string text, Control content)
    {
        var page = new TabPage(text) { BackColor = ManagerUi.Background };
        page.Controls.Add(content);
        _tabs.TabPages.Add(page);
    }

    private async Task OnTabChangedAsync()
    {
        await UpdateTopShiftStatusAsync();
        switch (_tabs.SelectedIndex)
        {
            case 0: await _pnlProducts.RefreshDataAsync(); break;
            case 1: await _pnlCategories.RefreshDataAsync(); break;
            case 2: await _pnlModifiers.RefreshDataAsync(); break;
            case 3: await _pnlPromotions.RefreshDataAsync(); break;
            case 4: await _pnlStaff.RefreshDataAsync(); break;
            case 5: await _pnlShifts.RefreshDataAsync(); break;
            case 6: await _pnlHistory.RefreshDataAsync(); break;
            case 7: await _pnlReports.RefreshDataAsync(); break;
        }
    }

    private async Task UpdateTopShiftStatusAsync()
    {
        try
        {
            var shift = await _controller.GetCurrentOpenShiftAsync();
            if (shift != null && shift.Status == ShiftStatus.Open)
            {
                _lblShift.Text = $"⏱️ Ca đang mở: {shift.ShiftId} (bắt đầu {shift.OpenedAt:HH:mm})";
                _lblShift.ForeColor = Color.FromArgb(74, 222, 128);
            }
            else
            {
                _lblShift.Text = "⏱️ Chưa có ca làm việc nào đang mở";
                _lblShift.ForeColor = Color.FromArgb(248, 113, 113);
            }
        }
        catch
        {
            _lblShift.Text = "⏱️ Không thể đọc trạng thái ca";
        }
    }

    private void Logout()
    {
        var confirm = MessageBox.Show(this, "Bạn có chắc chắn muốn đăng xuất?", "Xác nhận đăng xuất",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm == DialogResult.Yes)
        {
            _controller.Logout();
            Close();
        }
    }

    private async Task OpenCashierPosAsync()
    {
        try
        {
            var user = _controller.CurrentUser;
            if (user == null || !user.CanSell)
            {
                ManagerUi.Warning(this, "Tài khoản hiện tại không có quyền bán hàng tại quầy.");
                return;
            }

            var shift = await _controller.GetCurrentOpenShiftAsync();
            if (shift == null || shift.Status != ShiftStatus.Open)
            {
                var ask = MessageBox.Show(this,
                    "Hiện tại chưa có ca làm việc nào đang mở. Bạn có muốn chuyển sang tab 'Ca làm việc' để mở ca trước khi bán hàng không?",
                    "Chưa mở ca", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (ask == DialogResult.Yes)
                {
                    _tabs.SelectedIndex = 5; // Shift tab
                }
                return;
            }

            // Create integrated Cashier Controller with session bridged from Manager!
            var sessionProvider = new ManagerCashierSessionProvider(_controller);
            var cashierSource = new ManagerCashierDataSource(_controller, sessionProvider);
            var orderRepo = _orderRepository;
            var journal = new JsonCheckoutJournal(Path.Combine(_localDataDir, "pending-checkout.json"));

            var cashierController = new CashierController(cashierSource, orderRepo, journal);
            var cashierForm = new SalesForm(cashierController, _localDataDir, _receiptHistory);

            cashierForm.FormClosed += async (s, e) =>
            {
                await OnTabChangedAsync();
            };

            cashierForm.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ManagerUi.Error(this, ex);
        }
    }
}

