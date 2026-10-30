using System.Collections.Immutable;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Cashier.Core;
using Manager.Core;

namespace Manager.WinForms;

public sealed class ProductManagementPanel : UserControl
{
    private readonly ManagerController _controller;
    private readonly ComboBox _cbFilterCategory;
    private readonly TextBox _txtFilterName;
    private readonly ComboBox _cbFilterStatus;
    private readonly DataGridView _grid;

    // Edit fields
    private readonly TextBox _txtId;
    private readonly TextBox _txtName;
    private readonly ComboBox _cbCategory;
    private readonly NumericUpDown _numPrice;
    private readonly CheckBox _chkAvailable;
    private readonly Button _btnNew;
    private readonly Button _btnSave;
    private readonly Button _btnDelete;
    private readonly Button _btnCancel;
    private readonly Button _btnChooseImage;
    private readonly Button _btnRemoveImage;
    private readonly Label _lblImageStatus;
    private readonly PictureBox _picImagePreview;

    private ImmutableArray<MenuItem> _allProducts = [];
    private MenuItem? _selectedItem;
    private bool _isCreatingNew;
    private string? _selectedImageData;
    private string? _selectedImagePath;
    private bool _imageIsPending;

    public ProductManagementPanel(ManagerController controller)
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
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Data grid
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 180)); // Edit panel

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

        var lblCat = ManagerUi.Label("Danh mục:", 9.5f, true);
        lblCat.Margin = new Padding(3, 8, 3, 4);
        filterLayout.Controls.Add(lblCat);
        _cbFilterCategory = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Margin = new Padding(3, 4, 12, 4) };
        _cbFilterCategory.SelectedIndexChanged += (s, e) => ApplyFilter();
        filterLayout.Controls.Add(_cbFilterCategory);

        var lblName = ManagerUi.Label("Tên sản phẩm:", 9.5f, true);
        lblName.Margin = new Padding(3, 8, 3, 4);
        filterLayout.Controls.Add(lblName);
        _txtFilterName = new TextBox { Width = 180, Margin = new Padding(3, 4, 12, 4) };
        _txtFilterName.TextChanged += (s, e) => ApplyFilter();
        filterLayout.Controls.Add(_txtFilterName);

        var lblStatus = ManagerUi.Label("Trạng thái:", 9.5f, true);
        lblStatus.Margin = new Padding(3, 8, 3, 4);
        filterLayout.Controls.Add(lblStatus);
        _cbFilterStatus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, Margin = new Padding(3, 4, 12, 4) };
        _cbFilterStatus.Items.AddRange(["Tất cả", "Đang bán", "Ngừng bán"]);
        _cbFilterStatus.SelectedIndex = 0;
        _cbFilterStatus.SelectedIndexChanged += (s, e) => ApplyFilter();
        filterLayout.Controls.Add(_cbFilterStatus);

        filterPanel.Controls.Add(filterLayout);

        // 2. Data Grid
        _grid = ManagerUi.CreateDataGrid();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Mã món", DataPropertyName = "Id", Width = 120 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Sản phẩm", DataPropertyName = "Name", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Danh mục", DataPropertyName = "Category", Width = 140 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Giá bán", DataPropertyName = "PriceDisplay", Width = 130 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Trạng thái", DataPropertyName = "StatusDisplay", Width = 130 });
        _grid.SelectionChanged += (s, e) => OnSelectionChanged();

        // 3. Edit panel
        var editPanel = ManagerUi.CardPanel();
        editPanel.Dock = DockStyle.Fill;
        var editLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 3
        };
        editLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        editLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        editLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        editLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        editLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        editLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        editLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        _txtId = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
        _txtName = new TextBox { Dock = DockStyle.Fill };
        _cbCategory = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
        _numPrice = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = 0,
            Maximum = 100000000,
            Increment = 5000,
            ThousandsSeparator = true
        };
        _chkAvailable = new CheckBox { Text = "Đang kinh doanh (cho phép bán tại quầy)", AutoSize = true, Checked = true };

        editLayout.Controls.Add(ManagerUi.Label("Mã món:", 9.5f, true), 0, 0);
        editLayout.Controls.Add(_txtId, 1, 0);
        editLayout.Controls.Add(ManagerUi.Label("Tên món:", 9.5f, true), 2, 0);
        editLayout.Controls.Add(_txtName, 3, 0);

        editLayout.Controls.Add(ManagerUi.Label("Danh mục:", 9.5f, true), 0, 1);
        editLayout.Controls.Add(_cbCategory, 1, 1);
        editLayout.Controls.Add(ManagerUi.Label("Giá bán (đ):", 9.5f, true), 2, 1);
        editLayout.Controls.Add(_numPrice, 3, 1);

        var actionsPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        actionsPanel.Controls.Add(_chkAvailable);

        _btnNew = ManagerUi.Button("Thêm mới");
        _btnNew.Click += (s, e) => PrepareNew();
        actionsPanel.Controls.Add(_btnNew);

        _btnSave = ManagerUi.Button("Lưu", primary: true);
        _btnSave.Click += async (s, e) => await SaveItemAsync();
        actionsPanel.Controls.Add(_btnSave);

        _btnDelete = ManagerUi.Button("Xóa", isDanger: true);
        _btnDelete.Click += async (s, e) => await DeleteItemAsync();
        actionsPanel.Controls.Add(_btnDelete);

        _btnCancel = ManagerUi.Button("Hủy chỉnh sửa");
        _btnCancel.Click += (s, e) => CancelEdit();
        actionsPanel.Controls.Add(_btnCancel);

        _btnChooseImage = ManagerUi.Button("Chọn ảnh...");
        _btnChooseImage.Click += (s, e) => ChooseProductImage();
        actionsPanel.Controls.Add(_btnChooseImage);
        _btnRemoveImage = ManagerUi.Button("Bỏ ảnh");
        _btnRemoveImage.Click += (s, e) => ClearSelectedImage();
        actionsPanel.Controls.Add(_btnRemoveImage);
        _picImagePreview = new PictureBox
        {
            Width = 76,
            Height = 44,
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            Margin = new Padding(6, 3, 2, 3)
        };
        actionsPanel.Controls.Add(_picImagePreview);
        _lblImageStatus = ManagerUi.Label("Chưa chọn ảnh", 9, color: ManagerUi.Muted);
        actionsPanel.Controls.Add(_lblImageStatus);

        editLayout.Controls.Add(actionsPanel, 0, 2);
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
            PopulateCategories();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ManagerUi.Error(this, ex);
        }
    }

    private void PopulateCategories()
    {
        var cats = _allProducts.Select(x => x.Category).Distinct().OrderBy(x => x).ToList();
        _cbFilterCategory.Items.Clear();
        _cbFilterCategory.Items.Add("Tất cả danh mục");
        foreach (var c in cats) _cbFilterCategory.Items.Add(c);
        _cbFilterCategory.SelectedIndex = 0;

        _cbCategory.Items.Clear();
        foreach (var c in cats) _cbCategory.Items.Add(c);
    }

    private void ApplyFilter()
    {
        var filtered = _allProducts.AsEnumerable();

        if (_cbFilterCategory.SelectedIndex > 0)
        {
            var cat = _cbFilterCategory.SelectedItem?.ToString();
            filtered = filtered.Where(x => x.Category.Equals(cat, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(_txtFilterName.Text))
        {
            var kw = _txtFilterName.Text.Trim();
            filtered = filtered.Where(x => x.Name.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                                           x.Id.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }

        if (_cbFilterStatus.SelectedIndex == 1)
            filtered = filtered.Where(x => x.Available);
        else if (_cbFilterStatus.SelectedIndex == 2)
            filtered = filtered.Where(x => !x.Available);

        var rows = filtered.Select(x => new
        {
            x.Id,
            x.Name,
            x.Category,
            PriceDisplay = ManagerUi.FormatVnd(x.BasePrice),
            StatusDisplay = x.Available ? "Đang bán" : "Ngừng bán",
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
            _selectedItem = (MenuItem)item.Source;
            _isCreatingNew = false;
            _txtId.Text = _selectedItem.Id;
            _txtId.ReadOnly = true;
            _txtName.Text = _selectedItem.Name;
            _cbCategory.Text = _selectedItem.Category;
            _numPrice.Value = _selectedItem.BasePrice;
            _chkAvailable.Checked = _selectedItem.Available;
            _selectedImageData = _selectedItem.ImageData;
            _selectedImagePath = _selectedItem.ImagePath;
            _imageIsPending = false;
            UpdateImagePreview();
            UpdateImageStatus();
            _btnDelete.Enabled = true;
        }
    }

    private void PrepareNew()
    {
        _isCreatingNew = true;
        _selectedItem = null;
        _grid.ClearSelection();
        _txtId.ReadOnly = true;
        _txtId.Text = ManagerRules.NextMenuItemId(_allProducts);
        _txtName.Text = "";
        _numPrice.Value = 30000;
        _chkAvailable.Checked = true;
        _selectedImageData = null;
        _selectedImagePath = null;
        _imageIsPending = false;
        UpdateImagePreview();
        UpdateImageStatus();
        _btnDelete.Enabled = false;
        _txtName.Focus();
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
        _selectedItem = null;
        _txtId.Text = "";
        _txtName.Text = "";
        _cbCategory.Text = "";
        _numPrice.Value = 0;
        _chkAvailable.Checked = true;
        _selectedImageData = null;
        _selectedImagePath = null;
        _imageIsPending = false;
        UpdateImagePreview();
        UpdateImageStatus();
        _btnDelete.Enabled = false;
    }

    private async Task SaveItemAsync()
    {
        try
        {
            var id = _txtId.Text.Trim();
            var name = _txtName.Text.Trim();
            var category = _cbCategory.Text.Trim();
            var price = _numPrice.Value;
            var available = _chkAvailable.Checked;

            var groups = _selectedItem?.OptionGroups ?? [];

            var item = new MenuItem(id, name, category, price, available, groups)
            {
                ImagePath = _selectedImageData is null ? _selectedImagePath : null,
                ImageData = _selectedImageData
            };
            await _controller.SaveMenuItemAsync(item, _isCreatingNew);

            ManagerUi.Info(this, $"Đã lưu sản phẩm '{name}' thành công!");
            await RefreshDataAsync();
        }
        catch (Exception ex)
        {
            ManagerUi.Error(this, ex);
        }
    }

    private void ChooseProductImage()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Chọn ảnh sản phẩm",
            Filter = "Ảnh PNG hoặc JPEG|*.png;*.jpg;*.jpeg|Tất cả tệp|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _selectedImageData = EncodeProductImage(dialog.FileName);
            _selectedImagePath = null;
            _imageIsPending = true;
            UpdateImagePreview();
            UpdateImageStatus($"{Path.GetFileName(dialog.FileName)} (chưa lưu)");
        }
        catch (Exception ex)
        {
            ManagerUi.Error(this, ex, "Không thể dùng ảnh này");
        }
    }

    private void ClearSelectedImage()
    {
        _imageIsPending = _selectedImageData is not null || _selectedImagePath is not null;
        _selectedImageData = null;
        _selectedImagePath = null;
        UpdateImagePreview();
        UpdateImageStatus();
    }

    private void UpdateImageStatus(string? fileName = null)
    {
        if (_lblImageStatus is null) return;
        _lblImageStatus.Text = fileName ?? (_imageIsPending
            ? "Thay đổi ảnh (chưa lưu)"
            : _selectedImageData is not null ? "Ảnh đã lưu trong database"
            : _selectedImagePath is not null ? "Ảnh mẫu"
            : "Chưa có ảnh");
    }

    private void UpdateImagePreview()
    {
        Image? preview = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(_selectedImageData))
            {
                var bytes = Convert.FromBase64String(_selectedImageData);
                if (bytes.Length is > 0 and <= 8 * 1024 * 1024)
                {
                    using var stream = new MemoryStream(bytes, writable: false);
                    using var source = Image.FromStream(stream, false, true);
                    preview = new Bitmap(source);
                }
            }
            else if (!string.IsNullOrWhiteSpace(_selectedImagePath))
            {
                var relative = _selectedImagePath.Replace('\\', '/');
                var parts = relative.Split('/');
                if (parts.Length == 2 && parts[0].Equals("Images", StringComparison.OrdinalIgnoreCase) &&
                    parts[1] == Path.GetFileName(parts[1]))
                {
                    var fullPath = Path.Combine(AppContext.BaseDirectory, "Data", "Images", parts[1]);
                    if (File.Exists(fullPath))
                    {
                        using var source = Image.FromFile(fullPath);
                        preview = new Bitmap(source);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException or
            ArgumentException or ExternalException or OutOfMemoryException)
        {
            preview?.Dispose();
        }

        var oldPreview = _picImagePreview.Image;
        _picImagePreview.Image = preview;
        oldPreview?.Dispose();
    }

    private static string EncodeProductImage(string path)
    {
        const int maxInputBytes = 20 * 1024 * 1024;
        const int maxStoredBytes = 2 * 1024 * 1024;
        var file = new FileInfo(path);
        if (file.Length <= 0 || file.Length > maxInputBytes)
            throw new BusinessException("Ảnh phải có dung lượng dưới 20 MB.");

        using var source = Image.FromFile(path, useEmbeddedColorManagement: false);
        if (source.Width <= 0 || source.Height <= 0 || (long)source.Width * source.Height > 30_000_000)
            throw new BusinessException("Kích thước ảnh quá lớn, vui lòng chọn ảnh nhỏ hơn.");

        var scale = Math.Min(1d, 900d / Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, 0, 0, width, height);
        }

        using var stream = new MemoryStream();
        var jpeg = ImageCodecInfo.GetImageEncoders().First(codec => codec.MimeType == "image/jpeg");
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 82L);
        bitmap.Save(stream, jpeg, parameters);
        if (stream.Length > maxStoredBytes)
            throw new BusinessException("Ảnh sau khi nén vẫn vượt quá 2 MB. Hãy chọn ảnh khác.");
        return Convert.ToBase64String(stream.ToArray());
    }

    private async Task DeleteItemAsync()
    {
        if (_selectedItem == null) return;
        var confirm = MessageBox.Show(this,
            $"Bạn có chắc chắn muốn xóa sản phẩm '{_selectedItem.Name}' không?",
            "Xác nhận xóa", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (confirm == DialogResult.Yes)
        {
            try
            {
                await _controller.DeleteMenuItemAsync(_selectedItem.Id);
                ManagerUi.Info(this, "Đã xóa sản phẩm thành công!");
                await RefreshDataAsync();
            }
            catch (Exception ex)
            {
                ManagerUi.Error(this, ex);
            }
        }
    }
}

