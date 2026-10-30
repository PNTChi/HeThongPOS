using System.Collections.Immutable;
using Manager.Core;

namespace Manager.WinForms;

public sealed class StaffManagementPanel : UserControl
{
    private readonly ManagerController _controller;
    private readonly TextBox _txtFilterName;
    private readonly ComboBox _cbFilterRole;
    private readonly ComboBox _cbFilterStatus;
    private readonly DataGridView _grid;

    // Edit fields
    private readonly TextBox _txtStaffId;
    private readonly TextBox _txtFullName;
    private readonly TextBox _txtUsername;
    private readonly TextBox _txtPassword;
    private readonly ComboBox _cbRole;
    private readonly CheckBox _chkActive;
    private readonly CheckBox _chkCanSell;
    private readonly CheckBox _chkCanManageShift;

    private readonly Button _btnNew;
    private readonly Button _btnSave;
    private readonly Button _btnCancel;

    private ImmutableArray<StaffAccount> _allAccounts = [];
    private StaffAccount? _selectedAccount;
    private bool _isCreatingNew;

    public StaffManagementPanel(ManagerController controller)
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
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));  // Filter bar
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Grid
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 220)); // Edit panel

        // 1. Filter bar
        var filterPanel = ManagerUi.CardPanel(new Padding(12, 8, 12, 8));
        filterPanel.Dock = DockStyle.Fill;
        var filterLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        var lblName = ManagerUi.Label("Tên / Mã nhân viên:", 9.5f, true);
        lblName.Margin = new Padding(3, 8, 3, 4);
        filterLayout.Controls.Add(lblName);
        _txtFilterName = new TextBox { Width = 180, Margin = new Padding(3, 4, 12, 4) };
        _txtFilterName.TextChanged += (s, e) => ApplyFilter();
        filterLayout.Controls.Add(_txtFilterName);

        var lblRole = ManagerUi.Label("Vai trò:", 9.5f, true);
        lblRole.Margin = new Padding(3, 8, 3, 4);
        filterLayout.Controls.Add(lblRole);
        _cbFilterRole = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Margin = new Padding(3, 4, 12, 4) };
        _cbFilterRole.Items.AddRange(["Tất cả vai trò", "Quản lý (Manager)", "Thu ngân (Cashier)"]);
        _cbFilterRole.SelectedIndex = 0;
        _cbFilterRole.SelectedIndexChanged += (s, e) => ApplyFilter();
        filterLayout.Controls.Add(_cbFilterRole);

        var lblStatus = ManagerUi.Label("Trạng thái:", 9.5f, true);
        lblStatus.Margin = new Padding(3, 8, 3, 4);
        filterLayout.Controls.Add(lblStatus);
        _cbFilterStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, Margin = new Padding(3, 4, 12, 4) };
        _cbFilterStatus.Items.AddRange(["Tất cả", "Đang hoạt động", "Đã khóa"]);
        _cbFilterStatus.SelectedIndex = 0;
        _cbFilterStatus.SelectedIndexChanged += (s, e) => ApplyFilter();
        filterLayout.Controls.Add(_cbFilterStatus);

        filterPanel.Controls.Add(filterLayout);

        // 2. Data Grid
        _grid = ManagerUi.CreateDataGrid();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Mã NV", DataPropertyName = "StaffId", Width = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Họ và tên", DataPropertyName = "FullName", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tên đăng nhập", DataPropertyName = "Username", Width = 140 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Vai trò", DataPropertyName = "RoleDisplay", Width = 130 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Trạng thái", DataPropertyName = "StatusDisplay", Width = 130 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Quyền bán", DataPropertyName = "CanSellDisplay", Width = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Quyền ca", DataPropertyName = "CanShiftDisplay", Width = 110 });
        _grid.SelectionChanged += (s, e) => OnSelectionChanged();

        // 3. Edit panel
        var editPanel = ManagerUi.CardPanel();
        editPanel.Dock = DockStyle.Fill;
        var editLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 4
        };
        editLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        editLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        editLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        editLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        editLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        editLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        editLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        editLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        _txtStaffId = new TextBox { Dock = DockStyle.Fill };
        _txtFullName = new TextBox { Dock = DockStyle.Fill };
        _txtUsername = new TextBox { Dock = DockStyle.Fill };
        _txtPassword = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
        _cbRole = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbRole.Items.AddRange(["Quản lý (Manager)", "Thu ngân (Cashier)"]);
        _cbRole.SelectedIndex = 1;

        _chkActive = new CheckBox { Text = "Tài khoản hoạt động", AutoSize = true, Checked = true };
        _chkCanSell = new CheckBox { Text = "Quyền bán hàng", AutoSize = true, Checked = true };
        _chkCanManageShift = new CheckBox { Text = "Quyền quản lý ca", AutoSize = true, Checked = true };

        editLayout.Controls.Add(ManagerUi.FormLabel("Mã nhân viên:"), 0, 0);
        editLayout.Controls.Add(_txtStaffId, 1, 0);
        editLayout.Controls.Add(ManagerUi.FormLabel("Họ và tên:"), 2, 0);
        editLayout.Controls.Add(_txtFullName, 3, 0);

        editLayout.Controls.Add(ManagerUi.FormLabel("Tên đăng nhập:"), 0, 1);
        editLayout.Controls.Add(_txtUsername, 1, 1);
        editLayout.Controls.Add(ManagerUi.FormLabel("Mật khẩu:"), 2, 1);
        editLayout.Controls.Add(_txtPassword, 3, 1);

        editLayout.Controls.Add(ManagerUi.FormLabel("Vai trò:"), 0, 2);
        editLayout.Controls.Add(_cbRole, 1, 2);

        var rightsPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        rightsPanel.Controls.Add(_chkActive);
        rightsPanel.Controls.Add(_chkCanSell);
        rightsPanel.Controls.Add(_chkCanManageShift);
        editLayout.Controls.Add(rightsPanel, 2, 2);
        editLayout.SetColumnSpan(rightsPanel, 2);

        var actionsPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };

        _btnNew = ManagerUi.Button("Thêm nhân viên");
        _btnNew.Click += (s, e) => PrepareNew();
        actionsPanel.Controls.Add(_btnNew);

        _btnSave = ManagerUi.Button("Lưu thông tin", primary: true);
        _btnSave.Click += async (s, e) => await SaveStaffAsync();
        actionsPanel.Controls.Add(_btnSave);

        _btnCancel = ManagerUi.Button("Hủy chỉnh sửa");
        _btnCancel.Click += (s, e) => CancelEdit();
        actionsPanel.Controls.Add(_btnCancel);

        editLayout.Controls.Add(actionsPanel, 0, 3);
        editLayout.SetColumnSpan(actionsPanel, 4);

        editPanel.Controls.Add(editLayout);

        mainLayout.Controls.Add(filterPanel, 0, 0);
        mainLayout.Controls.Add(_grid, 0, 1);
        mainLayout.Controls.Add(editPanel, 0, 2);

        Controls.Add(mainLayout);
    }

    public async Task RefreshDataAsync()
    {
        try
        {
            _allAccounts = await _controller.GetAccountsAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ManagerUi.Error(this, ex);
        }
    }

    private void ApplyFilter()
    {
        var query = _allAccounts.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_txtFilterName.Text))
        {
            var kw = _txtFilterName.Text.Trim();
            query = query.Where(x => x.FullName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                                     x.StaffId.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                                     x.Username.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }

        if (_cbFilterRole.SelectedIndex == 1)
            query = query.Where(x => x.Role == UserRole.Manager);
        else if (_cbFilterRole.SelectedIndex == 2)
            query = query.Where(x => x.Role == UserRole.Cashier);

        if (_cbFilterStatus.SelectedIndex == 1)
            query = query.Where(x => x.IsActive);
        else if (_cbFilterStatus.SelectedIndex == 2)
            query = query.Where(x => !x.IsActive);

        var rows = query.Select(x => new
        {
            x.StaffId,
            x.FullName,
            x.Username,
            RoleDisplay = x.Role == UserRole.Manager ? "Quản lý" : "Thu ngân",
            StatusDisplay = x.IsActive ? "Hoạt động" : "Khóa",
            CanSellDisplay = x.CanSell ? "Có" : "Không",
            CanShiftDisplay = x.CanManageShift ? "Có" : "Không",
            Source = x
        }).ToList();

        _grid.DataSource = rows;
        if (rows.Count > 0)
        {
            _grid.Rows[0].Selected = true;
            OnSelectionChanged();
        }
        else
        {
            ClearEditFields();
        }
    }

    private void OnSelectionChanged()
    {
        if (_grid.CurrentRow?.DataBoundItem != null)
        {
            dynamic item = _grid.CurrentRow.DataBoundItem;
            _selectedAccount = (StaffAccount)item.Source;
            _isCreatingNew = false;
            _txtStaffId.Text = _selectedAccount.StaffId;
            _txtStaffId.ReadOnly = true;
            _txtFullName.Text = _selectedAccount.FullName;
            _txtUsername.Text = _selectedAccount.Username;
            _txtPassword.Text = _selectedAccount.Password;
            _cbRole.SelectedIndex = _selectedAccount.Role == UserRole.Manager ? 0 : 1;
            _chkActive.Checked = _selectedAccount.IsActive;
            _chkCanSell.Checked = _selectedAccount.CanSell;
            _chkCanManageShift.Checked = _selectedAccount.CanManageShift;
        }
    }

    private void PrepareNew()
    {
        _isCreatingNew = true;
        _selectedAccount = null;
        _grid.ClearSelection();
        _txtStaffId.ReadOnly = false;
        _txtStaffId.Text = "NV" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        _txtFullName.Text = "";
        _txtUsername.Text = "";
        _txtPassword.Text = "123456";
        _cbRole.SelectedIndex = 1;
        _chkActive.Checked = true;
        _chkCanSell.Checked = true;
        _chkCanManageShift.Checked = false;
        _txtFullName.Focus();
    }

    private void CancelEdit()
    {
        if (_grid.CurrentRow != null)
            OnSelectionChanged();
        else
            ClearEditFields();
    }

    private void ClearEditFields()
    {
        _selectedAccount = null;
        _txtStaffId.Text = "";
        _txtFullName.Text = "";
        _txtUsername.Text = "";
        _txtPassword.Text = "";
    }

    private async Task SaveStaffAsync()
    {
        try
        {
            var staffId = _txtStaffId.Text.Trim();
            var fullName = _txtFullName.Text.Trim();
            var username = _txtUsername.Text.Trim();
            var password = _txtPassword.Text;
            var role = _cbRole.SelectedIndex == 0 ? UserRole.Manager : UserRole.Cashier;
            var isActive = _chkActive.Checked;
            var canSell = _chkCanSell.Checked;
            var canShift = _chkCanManageShift.Checked;

            var account = new StaffAccount(staffId, fullName, username, password, role, isActive, canSell, canShift);
            await _controller.SaveAccountAsync(account, _isCreatingNew);

            ManagerUi.Info(this, $"Đã lưu tài khoản nhân viên '{fullName}' thành công!");
            await RefreshDataAsync();
        }
        catch (Exception ex)
        {
            ManagerUi.Error(this, ex);
        }
    }
}

