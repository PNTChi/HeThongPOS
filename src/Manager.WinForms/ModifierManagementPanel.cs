using System.Collections.Immutable;
using Cashier.Core;
using Manager.Core;

namespace Manager.WinForms;

public sealed class ModifierManagementPanel : UserControl
{
    private readonly ManagerController _controller;
    private readonly ComboBox _cbProducts;
    private readonly ComboBox _cbGroups;
    private readonly DataGridView _grid;

    // Group fields
    private readonly TextBox _txtGroupId;
    private readonly TextBox _txtGroupName;
    private readonly NumericUpDown _numMin;
    private readonly NumericUpDown _numMax;

    // Choice fields
    private readonly TextBox _txtChoiceId;
    private readonly TextBox _txtChoiceName;
    private readonly NumericUpDown _numExtraPrice;
    private readonly CheckBox _chkChoiceAvailable;

    private readonly Button _btnAddGroup;
    private readonly Button _btnAddChoice;
    private readonly Button _btnSave;
    private readonly Button _btnDelete;
    private readonly Button _btnCancel;

    private ImmutableArray<MenuItem> _allProducts = [];
    private MenuItem? _currentProduct;
    private List<OptionGroup> _workingGroups = [];

    public ModifierManagementPanel(ManagerController controller)
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

        var lblProduct = ManagerUi.Label("Sản phẩm:", 9.5f, true);
        lblProduct.Margin = new Padding(3, 8, 3, 4);
        filterLayout.Controls.Add(lblProduct);
        _cbProducts = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, Margin = new Padding(3, 4, 12, 4) };
        _cbProducts.SelectedIndexChanged += (s, e) => OnProductChanged();
        filterLayout.Controls.Add(_cbProducts);

        var lblGroup = ManagerUi.Label("Nhóm tùy chọn:", 9.5f, true);
        lblGroup.Margin = new Padding(3, 8, 3, 4);
        filterLayout.Controls.Add(lblGroup);
        _cbGroups = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Margin = new Padding(3, 4, 12, 4) };
        _cbGroups.SelectedIndexChanged += (s, e) => RefreshGrid();
        filterLayout.Controls.Add(_cbGroups);

        filterPanel.Controls.Add(filterLayout);

        // 2. Data Grid
        _grid = ManagerUi.CreateDataGrid();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Mã lựa chọn", DataPropertyName = "ChoiceId", Width = 130 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tùy chọn", DataPropertyName = "ChoiceName", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Nhóm", DataPropertyName = "GroupName", Width = 160 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Phụ thu", DataPropertyName = "ExtraPriceDisplay", Width = 140 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Trạng thái", DataPropertyName = "StatusDisplay", Width = 130 });
        _grid.SelectionChanged += (s, e) => OnGridSelectionChanged();

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

        // Group fields
        _txtGroupId = new TextBox { Dock = DockStyle.Fill };
        _txtGroupName = new TextBox { Dock = DockStyle.Fill };
        _numMin = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 0, Maximum = 10 };
        _numMax = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 1, Maximum = 20, Value = 1 };

        // Choice fields
        _txtChoiceId = new TextBox { Dock = DockStyle.Fill };
        _txtChoiceName = new TextBox { Dock = DockStyle.Fill };
        _numExtraPrice = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1000000, Increment = 1000, ThousandsSeparator = true };
        _chkChoiceAvailable = new CheckBox { Text = "Khả dụng", AutoSize = true, Checked = true };

        editLayout.Controls.Add(ManagerUi.FormLabel("Mã nhóm:"), 0, 0);
        editLayout.Controls.Add(_txtGroupId, 1, 0);
        editLayout.Controls.Add(ManagerUi.FormLabel("Tên nhóm:"), 2, 0);
        editLayout.Controls.Add(_txtGroupName, 3, 0);

        editLayout.Controls.Add(ManagerUi.FormLabel("Chọn tối thiểu:"), 0, 1);
        editLayout.Controls.Add(_numMin, 1, 1);
        editLayout.Controls.Add(ManagerUi.FormLabel("Chọn tối đa:"), 2, 1);
        editLayout.Controls.Add(_numMax, 3, 1);

        editLayout.Controls.Add(ManagerUi.FormLabel("Tên tùy chọn:"), 0, 2);
        editLayout.Controls.Add(_txtChoiceName, 1, 2);
        editLayout.Controls.Add(ManagerUi.FormLabel("Phụ thu (đ):"), 2, 2);
        editLayout.Controls.Add(_numExtraPrice, 3, 2);

        var actionsPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        actionsPanel.Controls.Add(_chkChoiceAvailable);

        _btnAddGroup = ManagerUi.Button("Tạo nhóm mới");
        _btnAddGroup.Click += (s, e) => AddNewGroup();
        actionsPanel.Controls.Add(_btnAddGroup);

        _btnAddChoice = ManagerUi.Button("Thêm tùy chọn");
        _btnAddChoice.Click += (s, e) => AddNewChoice();
        actionsPanel.Controls.Add(_btnAddChoice);

        _btnSave = ManagerUi.Button("Lưu cấu hình", primary: true);
        _btnSave.Click += async (s, e) => await SaveConfigurationAsync();
        actionsPanel.Controls.Add(_btnSave);

        _btnDelete = ManagerUi.Button("Xóa lựa chọn", isDanger: true);
        _btnDelete.Click += (s, e) => DeleteSelectedChoice();
        actionsPanel.Controls.Add(_btnDelete);

        _btnCancel = ManagerUi.Button("Hủy chỉnh sửa");
        _btnCancel.Click += (s, e) => OnProductChanged();
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
            _allProducts = await _controller.GetMenuAsync();
            _cbProducts.Items.Clear();
            foreach (var p in _allProducts)
                _cbProducts.Items.Add($"{p.Name} ({p.Id})");

            if (_cbProducts.Items.Count > 0)
                _cbProducts.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            ManagerUi.Error(this, ex);
        }
    }

    private void OnProductChanged()
    {
        if (_cbProducts.SelectedIndex < 0 || _cbProducts.SelectedIndex >= _allProducts.Length)
            return;

        _currentProduct = _allProducts[_cbProducts.SelectedIndex];
        _workingGroups = _currentProduct.OptionGroups.ToList();

        _cbGroups.Items.Clear();
        _cbGroups.Items.Add("Tất cả các nhóm");
        foreach (var g in _workingGroups)
            _cbGroups.Items.Add(g.Name);

        _cbGroups.SelectedIndex = 0;
        RefreshGrid();
    }

    private void RefreshGrid()
    {
        var rows = new List<dynamic>();

        var selectedGroupIdx = _cbGroups.SelectedIndex - 1;

        for (int i = 0; i < _workingGroups.Count; i++)
        {
            if (selectedGroupIdx >= 0 && selectedGroupIdx != i) continue;

            var g = _workingGroups[i];
            foreach (var c in g.Choices)
            {
                rows.Add(new
                {
                    ChoiceId = c.Id,
                    ChoiceName = c.Name,
                    GroupName = $"{g.Name} (Min: {g.Minimum}, Max: {g.Maximum})",
                    ExtraPriceDisplay = ManagerUi.FormatVnd(c.ExtraPrice),
                    StatusDisplay = c.Available ? "Khả dụng" : "Khóa",
                    Group = g,
                    Choice = c
                });
            }
        }

        _grid.DataSource = rows;
        if (rows.Count > 0)
        {
            _grid.Rows[0].Selected = true;
            OnGridSelectionChanged();
        }
        else
        {
            ClearChoiceInputs();
        }
    }

    private void OnGridSelectionChanged()
    {
        if (_grid.CurrentRow?.DataBoundItem != null)
        {
            dynamic item = _grid.CurrentRow.DataBoundItem;
            OptionGroup g = item.Group;
            OptionChoice c = item.Choice;

            _txtGroupId.Text = g.Id;
            _txtGroupName.Text = g.Name;
            _numMin.Value = g.Minimum;
            _numMax.Value = g.Maximum;

            _txtChoiceId.Text = c.Id;
            _txtChoiceName.Text = c.Name;
            _numExtraPrice.Value = c.ExtraPrice;
            _chkChoiceAvailable.Checked = c.Available;
            _btnDelete.Enabled = true;
        }
    }

    private void AddNewGroup()
    {
        var id = "GRP-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        _txtGroupId.Text = id;
        _txtGroupName.Text = "Nhóm tùy chọn mới";
        _numMin.Value = 0;
        _numMax.Value = 1;
        _txtChoiceName.Text = "Lựa chọn 1";
        _numExtraPrice.Value = 0;
        _chkChoiceAvailable.Checked = true;

        var newGroup = new OptionGroup(id, _txtGroupName.Text, 0, 1,
            [new("CH-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), "Lựa chọn 1", 0m, true)]);
        _workingGroups.Add(newGroup);

        _cbGroups.Items.Add(newGroup.Name);
        _cbGroups.SelectedIndex = _cbGroups.Items.Count - 1;
        RefreshGrid();
    }

    private void AddNewChoice()
    {
        if (_workingGroups.Count == 0)
        {
            AddNewGroup();
            return;
        }

        var activeGroupIdx = _cbGroups.SelectedIndex > 0 ? _cbGroups.SelectedIndex - 1 : 0;
        var group = _workingGroups[activeGroupIdx];

        var choiceId = "CH-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var choiceName = string.IsNullOrWhiteSpace(_txtChoiceName.Text) ? "Tùy chọn mới" : _txtChoiceName.Text.Trim();
        var extraPrice = _numExtraPrice.Value;
        var avail = _chkChoiceAvailable.Checked;

        var newChoice = new OptionChoice(choiceId, choiceName, extraPrice, avail);
        var updatedChoices = group.Choices.Add(newChoice);
        _workingGroups[activeGroupIdx] = group with { Choices = updatedChoices };

        RefreshGrid();
    }

    private void DeleteSelectedChoice()
    {
        if (_grid.CurrentRow?.DataBoundItem == null) return;
        dynamic item = _grid.CurrentRow.DataBoundItem;
        OptionGroup g = item.Group;
        OptionChoice c = item.Choice;

        var grpIdx = _workingGroups.FindIndex(x => x.Id == g.Id);
        if (grpIdx < 0) return;

        var remainingChoices = _workingGroups[grpIdx].Choices.Where(x => x.Id != c.Id).ToImmutableArray();
        if (remainingChoices.IsEmpty)
        {
            _workingGroups.RemoveAt(grpIdx);
            _cbGroups.Items.Remove(g.Name);
            _cbGroups.SelectedIndex = 0;
        }
        else
        {
            _workingGroups[grpIdx] = _workingGroups[grpIdx] with { Choices = remainingChoices };
        }

        RefreshGrid();
    }

    private void ClearChoiceInputs()
    {
        _txtChoiceId.Text = "";
        _txtChoiceName.Text = "";
        _numExtraPrice.Value = 0;
        _chkChoiceAvailable.Checked = true;
        _btnDelete.Enabled = false;
    }

    private async Task SaveConfigurationAsync()
    {
        if (_currentProduct == null) return;
        try
        {
            // Sync current active group inputs if edited
            if (!string.IsNullOrWhiteSpace(_txtGroupId.Text))
            {
                var gIdx = _workingGroups.FindIndex(x => x.Id.Equals(_txtGroupId.Text.Trim(), StringComparison.OrdinalIgnoreCase));
                if (gIdx >= 0)
                {
                    _workingGroups[gIdx] = _workingGroups[gIdx] with
                    {
                        Name = _txtGroupName.Text.Trim(),
                        Minimum = (int)_numMin.Value,
                        Maximum = (int)_numMax.Value
                    };
                }
            }

            var updatedProduct = _currentProduct with { OptionGroups = _workingGroups.ToImmutableArray() };
            await _controller.SaveMenuItemAsync(updatedProduct, isNew: false);

            ManagerUi.Info(this, $"Đã lưu cấu hình tùy chọn cho món '{updatedProduct.Name}' thành công!");
            await RefreshDataAsync();
        }
        catch (Exception ex)
        {
            ManagerUi.Error(this, ex);
        }
    }
}

