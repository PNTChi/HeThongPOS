using Manager.Core;

namespace Manager.WinForms;

public sealed class CategoryManagementPanel : UserControl
{
    private readonly ManagerController _controller;
    private readonly DataGridView _grid;
    private readonly TextBox _txtName;
    private readonly Button _btnAdd;
    private readonly Button _btnRename;
    private readonly Button _btnDelete;
    private List<string> _categories = [];
    private bool _loading;

    public CategoryManagementPanel(ManagerController controller)
    {
        _controller = controller;
        Dock = DockStyle.Fill;
        BackColor = ManagerUi.Background;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));

        var listCard = ManagerUi.CardPanel(new Padding(8));
        listCard.Dock = DockStyle.Fill;
        _grid = ManagerUi.CreateDataGrid();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Danh mục", DataPropertyName = "Name", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.SelectionChanged += (s, e) => OnSelectionChanged();
        listCard.Controls.Add(_grid);

        var editor = ManagerUi.CardPanel(new Padding(12, 10, 12, 8));
        editor.Dock = DockStyle.Fill;
        var editorLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        editorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        editorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        editorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editorLayout.Controls.Add(ManagerUi.FormLabel("Tên danh mục:"), 0, 0);
        _txtName = ManagerUi.TextBox();
        _txtName.Dock = DockStyle.Fill;
        editorLayout.Controls.Add(_txtName, 1, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 2, 0, 0) };
        _btnAdd = ManagerUi.Button("Thêm danh mục", primary: true);
        _btnRename = ManagerUi.Button("Đổi tên");
        _btnDelete = ManagerUi.Button("Xóa danh mục", isDanger: true);
        var btnRefresh = ManagerUi.Button("Làm mới");
        _btnAdd.Click += async (s, e) => await AddAsync();
        _btnRename.Click += async (s, e) => await RenameAsync();
        _btnDelete.Click += async (s, e) => await DeleteAsync();
        btnRefresh.Click += async (s, e) => await RefreshDataAsync();
        actions.Controls.AddRange([_btnAdd, _btnRename, _btnDelete, btnRefresh]);
        editorLayout.Controls.Add(actions, 1, 1);
        editor.Controls.Add(editorLayout);

        root.Controls.Add(listCard, 0, 0);
        root.Controls.Add(editor, 0, 1);
        Controls.Add(root);
        OnSelectionChanged();
    }

    public async Task RefreshDataAsync()
    {
        _loading = true;
        try
        {
            var selected = SelectedCategory;
            _categories = (await _controller.GetCategoriesAsync()).ToList();
            _grid.DataSource = _categories.Select(name => new CategoryRow { Name = name }).ToList();
            if (!string.IsNullOrEmpty(selected))
            {
                foreach (DataGridViewRow row in _grid.Rows)
                    if (string.Equals(row.Cells[0].Value?.ToString(), selected, StringComparison.OrdinalIgnoreCase))
                    {
                        row.Selected = true;
                        _grid.CurrentCell = row.Cells[0];
                        break;
                    }
            }
            if (_grid.SelectedRows.Count == 0) _txtName.Clear();
        }
        catch (Exception ex) { ManagerUi.Error(this, ex); }
        finally { _loading = false; OnSelectionChanged(); }
    }

    private string? SelectedCategory => _grid.SelectedRows.Count == 0
        ? null
        : _grid.SelectedRows[0].Cells[0].Value?.ToString();

    private void OnSelectionChanged()
    {
        if (_loading) return;
        var selected = SelectedCategory;
        if (selected is not null) _txtName.Text = selected;
        _btnRename.Enabled = selected is not null;
        _btnDelete.Enabled = selected is not null;
    }

    private async Task AddAsync()
    {
        try
        {
            await _controller.CreateCategoryAsync(_txtName.Text);
            ManagerUi.Info(this, "Đã thêm danh mục.");
            await RefreshDataAsync();
        }
        catch (Exception ex) { ManagerUi.Error(this, ex); }
    }

    private async Task RenameAsync()
    {
        var selected = SelectedCategory;
        if (selected is null) return;
        try
        {
            await _controller.RenameCategoryAsync(selected, _txtName.Text);
            ManagerUi.Info(this, "Đã đổi tên danh mục và cập nhật các sản phẩm/khuyến mãi liên quan.");
            await RefreshDataAsync();
        }
        catch (Exception ex) { ManagerUi.Error(this, ex); }
    }

    private async Task DeleteAsync()
    {
        var selected = SelectedCategory;
        if (selected is null) return;
        if (MessageBox.Show(this, $"Xóa danh mục '{selected}'?", "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            await _controller.DeleteCategoryAsync(selected);
            ManagerUi.Info(this, "Đã xóa danh mục.");
            _txtName.Clear();
            await RefreshDataAsync();
        }
        catch (Exception ex) { ManagerUi.Error(this, ex); }
    }

    private sealed class CategoryRow
    {
        public string Name { get; init; } = string.Empty;
    }
}
