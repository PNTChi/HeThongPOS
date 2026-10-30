using System.Collections.Immutable;
using Cashier.Core;
using Manager.Core;

namespace Manager.WinForms;

public sealed class PromotionManagementPanel : UserControl
{
    private readonly ManagerController _controller;
    private readonly TextBox _txtFilterName;
    private readonly ComboBox _cbFilterStatus;
    private readonly DataGridView _grid;

    // Edit fields
    private readonly TextBox _txtCode;
    private readonly TextBox _txtDescription;
    private readonly ComboBox _cbKind;
    private readonly ComboBox _cbScope;
    private readonly CheckedListBox _lstTargets;
    private readonly TableLayoutPanel _editLayout;
    private readonly Label _lblTargets;
    private readonly NumericUpDown _numValue;
    private readonly NumericUpDown _numMinSubtotal;
    private readonly DateTimePicker _dtpStart;
    private readonly DateTimePicker _dtpEnd;
    private readonly CheckBox _chkActive;

    private readonly Button _btnNew;
    private readonly Button _btnSave;
    private readonly Button _btnDelete;
    private readonly Button _btnCancel;

    private ImmutableArray<Promotion> _allPromos = [];
    private ImmutableArray<MenuItem> _menuItems = [];
    private Promotion? _selectedPromo;
    private bool _isCreatingNew;

    public PromotionManagementPanel(ManagerController controller)
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
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 330)); // Edit panel

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

        var lblName = ManagerUi.Label("Tên / Mã khuyến mãi:", 9.5f, true);
        lblName.Margin = new Padding(3, 8, 3, 4);
        filterLayout.Controls.Add(lblName);
        _txtFilterName = new TextBox { Width = 200, Margin = new Padding(3, 4, 12, 4) };
        _txtFilterName.TextChanged += (s, e) => ApplyFilter();
        filterLayout.Controls.Add(_txtFilterName);

        var lblStatus = ManagerUi.Label("Trạng thái:", 9.5f, true);
        lblStatus.Margin = new Padding(3, 8, 3, 4);
        filterLayout.Controls.Add(lblStatus);
        _cbFilterStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, Margin = new Padding(3, 4, 12, 4) };
        _cbFilterStatus.Items.AddRange(["Tất cả", "Đang hiệu lực", "Hết hạn / Khóa"]);
        _cbFilterStatus.SelectedIndex = 0;
        _cbFilterStatus.SelectedIndexChanged += (s, e) => ApplyFilter();
        filterLayout.Controls.Add(_cbFilterStatus);

        filterPanel.Controls.Add(filterLayout);

        // 2. Data Grid
        _grid = ManagerUi.CreateDataGrid();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Mã KM", DataPropertyName = "Code", Width = 120 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Chương trình", DataPropertyName = "Description", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Mức giảm", DataPropertyName = "DiscountDisplay", Width = 130 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Đơn tối thiểu", DataPropertyName = "MinSubtotalDisplay", Width = 130 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Phạm vi", DataPropertyName = "ScopeDisplay", Width = 180 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Hiệu lực", DataPropertyName = "ValidityDisplay", Width = 200 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Trạng thái", DataPropertyName = "StatusDisplay", Width = 120 });
        _grid.SelectionChanged += (s, e) => OnSelectionChanged();

        // 3. Edit panel
        var editPanel = ManagerUi.CardPanel();
        editPanel.Dock = DockStyle.Fill;
        _editLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 6
        };
        _editLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        _editLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _editLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        _editLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        _editLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _editLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _editLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _editLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _editLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 106));
        _editLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        _txtCode = new TextBox { Dock = DockStyle.Fill, CharacterCasing = CharacterCasing.Upper };
        _txtDescription = new TextBox { Dock = DockStyle.Fill };
        _cbKind = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbKind.Items.AddRange(["Phần trăm (%)", "Số tiền cố định (đ)"]);
        _cbKind.SelectedIndex = 0;
        _cbKind.SelectedIndexChanged += (s, e) => UpdateValueControl();

        _cbScope = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbScope.Items.AddRange(["Toàn đơn hàng", "Theo sản phẩm", "Theo danh mục"]);
        _cbScope.SelectedIndex = 0;
        _cbScope.SelectedIndexChanged += (s, e) => RefreshTargetChoices();
        _lstTargets = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            IntegralHeight = false,
            HorizontalScrollbar = true
        };
        _lblTargets = ManagerUi.FormLabel("Chọn áp dụng:");

        _numValue = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 1, Maximum = 100 };
        _numMinSubtotal = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100000000, Increment = 10000, ThousandsSeparator = true };

        _dtpStart = new DateTimePicker { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy HH:mm" };
        _dtpEnd = new DateTimePicker { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy HH:mm" };
        _chkActive = new CheckBox { Text = "Kích hoạt áp dụng", AutoSize = true, Checked = true };

        _editLayout.Controls.Add(ManagerUi.FormLabel("Mã khuyến mãi:"), 0, 0);
        _editLayout.Controls.Add(_txtCode, 1, 0);
        _editLayout.Controls.Add(ManagerUi.FormLabel("Mô tả / Tên CT:"), 2, 0);
        _editLayout.Controls.Add(_txtDescription, 3, 0);

        _editLayout.Controls.Add(ManagerUi.FormLabel("Loại giảm giá:"), 0, 1);
        _editLayout.Controls.Add(_cbKind, 1, 1);
        _editLayout.Controls.Add(ManagerUi.FormLabel("Mức giảm:"), 2, 1);
        _editLayout.Controls.Add(_numValue, 3, 1);

        _editLayout.Controls.Add(ManagerUi.FormLabel("Bắt đầu hiệu lực:"), 0, 2);
        _editLayout.Controls.Add(_dtpStart, 1, 2);
        _editLayout.Controls.Add(ManagerUi.FormLabel("Kết thúc hiệu lực:"), 2, 2);
        _editLayout.Controls.Add(_dtpEnd, 3, 2);
        _editLayout.Controls.Add(ManagerUi.FormLabel("Đơn tối thiểu (đ):"), 0, 3);
        _editLayout.Controls.Add(_numMinSubtotal, 1, 3);
        _editLayout.Controls.Add(ManagerUi.FormLabel("Phạm vi áp dụng:"), 2, 3);
        _editLayout.Controls.Add(_cbScope, 3, 3);
        _editLayout.Controls.Add(_lblTargets, 0, 4);
        _editLayout.Controls.Add(_lstTargets, 1, 4);
        _editLayout.SetColumnSpan(_lstTargets, 3);

        var actionsPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        actionsPanel.Controls.Add(_chkActive);
        _btnNew = ManagerUi.Button("Thêm chương trình");
        _btnNew.Click += (s, e) => PrepareNew();
        actionsPanel.Controls.Add(_btnNew);

        _btnSave = ManagerUi.Button("Lưu", primary: true);
        _btnSave.Click += async (s, e) => await SavePromoAsync();
        actionsPanel.Controls.Add(_btnSave);

        _btnDelete = ManagerUi.Button("Xóa", isDanger: true);
        _btnDelete.Click += async (s, e) => await DeletePromoAsync();
        actionsPanel.Controls.Add(_btnDelete);

        _btnCancel = ManagerUi.Button("Hủy chỉnh sửa");
        _btnCancel.Click += (s, e) => CancelEdit();
        actionsPanel.Controls.Add(_btnCancel);

        _editLayout.Controls.Add(actionsPanel, 0, 5);
        _editLayout.SetColumnSpan(actionsPanel, 4);

        editPanel.Controls.Add(_editLayout);

        mainLayout.Controls.Add(filterPanel, 0, 0);
        mainLayout.Controls.Add(_grid, 0, 1);
        mainLayout.Controls.Add(editPanel, 0, 2);

        Controls.Add(mainLayout);
        RefreshTargetChoices();
        var tips = new ToolTip();
        tips.SetToolTip(_numMinSubtotal, "Điều kiện tối thiểu tính trên tổng đơn hàng.");
        tips.SetToolTip(_cbScope,
            "Nếu chọn sản phẩm/danh mục, mức giảm chỉ tính trên các món khớp phạm vi. Chọn cả sản phẩm và danh mục được hiểu là khớp một trong hai.");
    }

    private void UpdateValueControl()
    {
        if (_cbKind.SelectedIndex == 0) // Percentage
        {
            _numValue.Maximum = 100;
            _numValue.Increment = 5;
            _numValue.ThousandsSeparator = false;
        }
        else // Fixed Amount
        {
            _numValue.Maximum = 10000000;
            _numValue.Increment = 5000;
            _numValue.ThousandsSeparator = true;
        }
    }

    public async Task RefreshDataAsync()
    {
        try
        {
            _menuItems = await _controller.GetMenuAsync();
            _allPromos = await _controller.GetPromotionsAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ManagerUi.Error(this, ex);
        }
    }

    private void ApplyFilter()
    {
        var now = DateTimeOffset.Now;
        var query = _allPromos.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_txtFilterName.Text))
        {
            var kw = _txtFilterName.Text.Trim();
            query = query.Where(x => x.Code.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                                     x.Description.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }

        if (_cbFilterStatus.SelectedIndex == 1)
            query = query.Where(x => x.Active && x.StartsAt <= now && x.EndsAt >= now);
        else if (_cbFilterStatus.SelectedIndex == 2)
            query = query.Where(x => !x.Active || x.EndsAt < now || x.StartsAt > now);

        var rows = query.Select(x =>
        {
            var isExpired = x.EndsAt < now;
            var isFuture = x.StartsAt > now;
            var statusText = !x.Active ? "Đang khóa" : (isExpired ? "Hết hạn" : (isFuture ? "Chưa tới hạn" : "Đang chạy"));
            var discountText = x.Kind == DiscountKind.Percentage ? $"{x.Value}%" : ManagerUi.FormatVnd(x.Value);

            return new
            {
                x.Code,
                x.Description,
                DiscountDisplay = discountText,
                MinSubtotalDisplay = ManagerUi.FormatVnd(x.MinimumSubtotal),
                ScopeDisplay = GetScopeDisplay(x),
                ValidityDisplay = $"{x.StartsAt:dd/MM/yyyy} - {x.EndsAt:dd/MM/yyyy}",
                StatusDisplay = statusText,
                Source = x
            };
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
            _selectedPromo = (Promotion)item.Source;
            _isCreatingNew = false;
            _txtCode.Text = _selectedPromo.Code;
            _txtCode.ReadOnly = true;
            _txtDescription.Text = _selectedPromo.Description;
            _cbKind.SelectedIndex = _selectedPromo.Kind == DiscountKind.Percentage ? 0 : 1;
            UpdateValueControl();
            _numValue.Value = _selectedPromo.Value;
            _numMinSubtotal.Value = _selectedPromo.MinimumSubtotal;
            _dtpStart.Value = _selectedPromo.StartsAt.LocalDateTime;
            _dtpEnd.Value = _selectedPromo.EndsAt.LocalDateTime;
            _chkActive.Checked = _selectedPromo.Active;
            _cbScope.SelectedIndex = !_selectedPromo.ProductIds.IsDefaultOrEmpty ? 1 :
                !_selectedPromo.Categories.IsDefaultOrEmpty ? 2 : 0;
            var selectedTargets = _cbScope.SelectedIndex == 1
                ? _selectedPromo.ProductIds
                : _selectedPromo.Categories;
            RefreshTargetChoices(selectedTargets.IsDefault ? [] : selectedTargets);
            _btnDelete.Enabled = true;
        }
    }

    private void PrepareNew()
    {
        _isCreatingNew = true;
        _selectedPromo = null;
        _grid.ClearSelection();
        _txtCode.ReadOnly = false;
        _txtCode.Text = "KM" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        _txtDescription.Text = "";
        _cbKind.SelectedIndex = 0;
        _numValue.Value = 10;
        _numMinSubtotal.Value = 40000;
        _cbScope.SelectedIndex = 0;
        RefreshTargetChoices();
        _dtpStart.Value = DateTime.Now;
        _dtpEnd.Value = DateTime.Now.AddDays(30);
        _chkActive.Checked = true;
        _btnDelete.Enabled = false;
        _txtDescription.Focus();
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
        _selectedPromo = null;
        _txtCode.Text = "";
        _txtDescription.Text = "";
        _numValue.Value = 1;
        _numMinSubtotal.Value = 0;
        _cbScope.SelectedIndex = 0;
        RefreshTargetChoices();
        _btnDelete.Enabled = false;
    }

    private async Task SavePromoAsync()
    {
        try
        {
            var code = _txtCode.Text.Trim();
            var desc = _txtDescription.Text.Trim();
            var kind = _cbKind.SelectedIndex == 0 ? DiscountKind.Percentage : DiscountKind.FixedAmount;
            var val = _numValue.Value;
            var minSub = _numMinSubtotal.Value;
            var startsAt = new DateTimeOffset(_dtpStart.Value);
            var endsAt = new DateTimeOffset(_dtpEnd.Value);
            var active = _chkActive.Checked;
            var selectedTargets = _lstTargets.CheckedItems.Cast<PromoTargetOption>()
                .Select(target => target.Id).ToImmutableArray();
            if (_cbScope.SelectedIndex != 0 && selectedTargets.IsEmpty)
                throw new BusinessException("Hãy chọn ít nhất một sản phẩm hoặc danh mục áp dụng.");
            var productIds = _cbScope.SelectedIndex == 1 ? selectedTargets : ImmutableArray<string>.Empty;
            var categories = _cbScope.SelectedIndex == 2 ? selectedTargets : ImmutableArray<string>.Empty;

            var promo = new Promotion(code, desc, kind, val, minSub, startsAt, endsAt, active,
                productIds, categories);
            await _controller.SavePromotionAsync(promo, _isCreatingNew);

            ManagerUi.Info(this, $"Đã lưu chương trình khuyến mãi '{code}' thành công!");
            await RefreshDataAsync();
        }
        catch (Exception ex)
        {
            ManagerUi.Error(this, ex);
        }
    }

    private async Task DeletePromoAsync()
    {
        if (_selectedPromo == null) return;
        var confirm = MessageBox.Show(this,
            $"Bạn có chắc chắn muốn xóa khuyến mãi '{_selectedPromo.Code}' không?",
            "Xác nhận xóa", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (confirm == DialogResult.Yes)
        {
            try
            {
                await _controller.DeletePromotionAsync(_selectedPromo.Code);
                ManagerUi.Info(this, "Đã xóa khuyến mãi thành công!");
                await RefreshDataAsync();
            }
            catch (Exception ex)
            {
                ManagerUi.Error(this, ex);
            }
        }
    }

    private void RefreshTargetChoices(IEnumerable<string>? selected = null)
    {
        if (_lstTargets is null || _editLayout is null) return;

        var showTargets = _cbScope.SelectedIndex is 1 or 2;
        _lblTargets.Visible = showTargets;
        _lstTargets.Visible = showTargets;
        _editLayout.RowStyles[4].Height = showTargets ? 106 : 0;
        _lstTargets.Items.Clear();
        if (showTargets)
        {
            var selectedSet = (selected ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (_cbScope.SelectedIndex == 1)
            {
                foreach (var item in _menuItems.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
                {
                    _lstTargets.Items.Add(new PromoTargetOption(item.Id, $"{item.Name} ({item.Id})"),
                        selectedSet.Contains(item.Id));
                }
            }
            else
            {
                foreach (var category in _menuItems.Select(item => item.Category)
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(category => category, StringComparer.OrdinalIgnoreCase))
                {
                    _lstTargets.Items.Add(new PromoTargetOption(category, category), selectedSet.Contains(category));
                }
            }
        }
        _editLayout.PerformLayout();
    }

    private static string GetScopeDisplay(Promotion promo)
    {
        var productCount = promo.ProductIds.IsDefault ? 0 : promo.ProductIds.Length;
        var categoryCount = promo.Categories.IsDefault ? 0 : promo.Categories.Length;
        if (productCount == 0 && categoryCount == 0) return "Toàn đơn hàng";
        if (productCount > 0 && categoryCount > 0) return $"{productCount} SP / {categoryCount} danh mục";
        if (productCount > 0) return $"{productCount} sản phẩm";
        return $"{categoryCount} danh mục";
    }

    private sealed record PromoTargetOption(string Id, string Display)
    {
        public override string ToString() => Display;
    }
}

