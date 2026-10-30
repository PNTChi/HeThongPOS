using Cashier.Core;

namespace Cashier.WinForms;

public sealed class SalesForm : Form
{
    private readonly CashierController _controller;
    private readonly string _dataDirectory;
    private readonly IReceiptHistory? _historySource;
    private readonly FlowLayoutPanel _products = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
    private readonly TextBox _search = new() { Dock = DockStyle.Fill, PlaceholderText = "Tìm tên món (có hoặc không dấu)…" };
    private readonly ComboBox _category = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _promoCode = new() { Dock = DockStyle.Fill, PlaceholderText = "Nhập mã khuyến mãi" };
    private readonly DataGridView _cart = new();
    private readonly Label _session = Ui.Label();
    private readonly Label _status = Ui.Label();
    private readonly Label _subtotal = Ui.Label();
    private readonly Label _discount = Ui.Label();
    private readonly Label _payable = Ui.Label("", 20, true);
    private readonly Label _promoStatus = Ui.Label();
    private readonly Label _menuCount = Ui.Label();
    private readonly Label _cartCount = Ui.Label();
    private readonly Label _emptyCart = Ui.Label("Giỏ hàng trống\nChọn món bên trái để bắt đầu.", 11);
    private readonly Button _checkout = Ui.Button("Thanh toán  •  F9", true);
    private readonly Button _newOrder = Ui.Button("Đơn mới  •  F2");
    private readonly Button _refresh = Ui.Button("Tải lại thực đơn");
    private readonly Button _history = Ui.Button("Lịch sử hóa đơn");
    private readonly Button _applyPromo = Ui.Button("Áp dụng");
    private readonly Button _removePromo = Ui.Button("Bỏ mã");
    private readonly Button _increase = Ui.Button("+ SL");
    private readonly Button _decrease = Ui.Button("− SL");
    private readonly Button _edit = Ui.Button("Sửa món");
    private readonly Button _remove = Ui.Button("Xóa món");
    private readonly Button _unlock = Ui.Button("Mở lại giỏ chưa lưu");
    private bool _loaded;
    private bool _busy;
    private bool _rendering;
    private bool _populatingCategories;
    private System.Collections.Immutable.ImmutableArray<MenuItem> _renderedMenu;
    private readonly ToolTip _tips = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CashierWindowLease? _windowLease;
    private bool _closed;
    private bool _resourcesDisposed;

    public SalesForm(CashierController controller, string dataDirectory, IReceiptHistory? historySource = null)
    {
        _controller = controller;
        _dataDirectory = dataDirectory;
        _historySource = historySource;
        try
        {
            _windowLease = CashierWindowLease.Acquire(dataDirectory);
            Ui.FormStyle(this, "POS Nhóm 11 — Cashier", new Size(1360, 800));
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1100, 680);
            KeyPreview = true;
            var root = Ui.Rows(SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize);
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2, RowCount = 2, BackColor = Ui.Surface, Padding = new Padding(16, 8, 16, 8)
            };
            header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.Controls.Add(Ui.Label("BÁN HÀNG TẠI QUẦY", 20, true), 0, 0);
            _session.ForeColor = Ui.Muted;
            _session.Margin = new Padding(4, 0, 4, 4);
            header.Controls.Add(_session, 0, 1);
            header.SetColumnSpan(_session, 2);
            Ui.WrapToParent(_session, header);
            var tools = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.None,
                Anchor = AnchorStyles.Top | AnchorStyles.Right, WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight, Padding = Padding.Empty
            };
            _newOrder.Width = 145;
            _refresh.Width = 160;
            _history.Width = 155;
            tools.Controls.AddRange([_newOrder, _refresh, _history]);
            header.Controls.Add(tools, 1, 0);
            root.Controls.Add(header, 0, 0);
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), Margin = Padding.Empty };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.Controls.Add(BuildMenu(), 0, 0);
            content.Controls.Add(BuildCart(), 1, 0);
            root.Controls.Add(content, 0, 1);
            _status.Dock = DockStyle.Fill;
            _status.BackColor = Ui.Soft;
            _status.Margin = Padding.Empty;
            _status.Padding = new Padding(12, 6, 12, 6);
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Text = "Đang tải dữ liệu Cashier…";
            root.Controls.Add(_status, 0, 2);
            Ui.WrapToParent(_status, root);
            Controls.Add(root);
            _search.TextChanged += (_, _) => RenderProducts();
            _category.SelectedIndexChanged += (_, _) => { if (!_populatingCategories) RenderProducts(); };
            _newOrder.Click += (_, _) => NewOrder();
            _refresh.Click += async (_, _) => await ReloadAsync();
            _history.Click += async (_, _) => await OpenHistoryAsync();
            _applyPromo.Click += (_, _) => ApplyPromotion();
            _removePromo.Click += (_, _) => Run(_controller.RemovePromotion);
            _increase.Click += (_, _) => AdjustQuantity(1);
            _decrease.Click += (_, _) => AdjustQuantity(-1);
            _edit.Click += (_, _) => EditSelected();
            _remove.Click += (_, _) => DeleteSelected();
            _unlock.Click += async (_, _) => await UnlockAsync();
            _checkout.Click += (_, _) => OpenPayment();
            _cart.SelectionChanged += (_, _) => { if (!_rendering) SetActionState(); };
            _cart.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditSelected(); };
            _promoCode.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { _applyPromo.PerformClick(); e.SuppressKeyPress = true; } };
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.F2) { NewOrder(); e.Handled = true; }
                if (e.KeyCode == Keys.F4) { _search.Focus(); _search.SelectAll(); e.Handled = true; }
                if (e.KeyCode == Keys.F9) { OpenPayment(); e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.Delete && _cart.Focused) { DeleteSelected(); e.Handled = true; }
            };
            _tips.SetToolTip(_refresh, "Đọc dữ liệu đã được cấu hình; không chỉnh sửa thực đơn.");
            _tips.SetToolTip(_decrease, "Số lượng tối thiểu là 1. Dùng Xóa món nếu muốn bỏ dòng.");
            _tips.SetToolTip(_history, "Xem/in hóa đơn đã lưu trên máy; tạo giỏ tương tự dùng giá hiện tại. Không phải lịch sử theo khách.");
            _tips.SetToolTip(_status, "Dữ liệu chạy thử lưu tại: " + _dataDirectory);
            _products.SizeChanged += (_, _) => ResizeProductCards();
            Shown += async (_, _) => await LoadAsync();
            FormClosing += (_, e) =>
            {
                if (_busy) { e.Cancel = true; return; }
                if (!_loaded) return;
                if (_controller.State == CheckoutState.Ready && _controller.Lines.Count > 0 &&
                    MessageBox.Show(this, "Giỏ chưa thanh toán sẽ không được giữ. Đóng ứng dụng?", "Xác nhận",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) e.Cancel = true;
                if (_controller.PendingReceipt is not null && _controller.State != CheckoutState.Paid &&
                    MessageBox.Show(this, "Giao dịch đang chờ sẽ được giữ để kiểm tra khi mở lại. Không thu lại tiền. Đóng ứng dụng?",
                        "Giao dịch đang chờ", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) e.Cancel = true;
            };
            SetActionState();
        }
        catch { Dispose(); throw; }
    }

    private Control BuildMenu()
    {
        var panel = Ui.Rows(SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent);
        panel.BackColor = Ui.Surface;
        panel.Padding = new Padding(12);
        panel.Margin = new Padding(0, 0, 8, 0);
        var menuHeading = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        menuHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        menuHeading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        menuHeading.Controls.Add(Ui.Label("Thực đơn", 16, true), 0, 0);
        _menuCount.ForeColor = Ui.Muted;
        menuHeading.Controls.Add(_menuCount, 1, 0);
        panel.Controls.Add(menuHeading, 0, 0);
        var filters = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 8, 0, 12) };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        filters.Controls.Add(_search, 0, 0);
        filters.Controls.Add(_category, 1, 0);
        panel.Controls.Add(filters, 0, 1);
        panel.Controls.Add(_products, 0, 2);
        return panel;
    }

    private Control BuildCart()
    {
        var panel = Ui.Rows(SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize, SizeType.AutoSize,
            SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize);
        panel.BackColor = Ui.Surface;
        panel.Padding = new Padding(12);
        panel.Margin = new Padding(4, 0, 0, 0);
        var cartHeading = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        cartHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cartHeading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cartHeading.Controls.Add(Ui.Label("Giỏ hàng", 16, true), 0, 0);
        _cartCount.ForeColor = Ui.Muted;
        cartHeading.Controls.Add(_cartCount, 1, 0);
        panel.Controls.Add(cartHeading, 0, 0);
        _cart.Dock = DockStyle.Fill;
        _cart.AllowUserToAddRows = false;
        _cart.AllowUserToDeleteRows = false;
        _cart.AllowUserToResizeRows = false;
        _cart.ReadOnly = true;
        _cart.MultiSelect = false;
        _cart.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _cart.RowHeadersVisible = false;
        _cart.BackgroundColor = Ui.Surface;
        _cart.GridColor = Ui.Border;
        _cart.BorderStyle = BorderStyle.None;
        _cart.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _cart.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _cart.EnableHeadersVisualStyles = false;
        _cart.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _cart.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 8, 4, 8);
        _cart.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _cart.ColumnHeadersDefaultCellStyle.BackColor = Ui.Soft;
        _cart.ColumnHeadersDefaultCellStyle.ForeColor = Ui.Brand;
        _cart.ColumnHeadersDefaultCellStyle.SelectionBackColor = Ui.Soft;
        _cart.ColumnHeadersDefaultCellStyle.SelectionForeColor = Ui.Brand;
        _cart.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        _cart.DefaultCellStyle.SelectionBackColor = Ui.Selection;
        _cart.DefaultCellStyle.SelectionForeColor = Ui.Ink;
        _cart.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _cart.DefaultCellStyle.Padding = new Padding(5);
        _cart.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _cart.Columns.Add(new DataGridViewTextBoxColumn { Name = "Item", HeaderText = "Món / tùy chọn", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 140, SortMode = DataGridViewColumnSortMode.NotSortable });
        _cart.Columns.Add(new DataGridViewTextBoxColumn { Name = "Qty", HeaderText = "SL", Width = 47, SortMode = DataGridViewColumnSortMode.NotSortable });
        _cart.Columns.Add(new DataGridViewTextBoxColumn { Name = "Price", HeaderText = "Đơn giá", Width = 94, SortMode = DataGridViewColumnSortMode.NotSortable });
        _cart.Columns.Add(new DataGridViewTextBoxColumn { Name = "Total", HeaderText = "Thành tiền", Width = 108, SortMode = DataGridViewColumnSortMode.NotSortable });
        _cart.Columns[1].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _cart.Columns[2].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _cart.Columns[3].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        var cartBody = new Panel { Dock = DockStyle.Fill };
        _emptyCart.Dock = DockStyle.Fill;
        _emptyCart.AutoSize = false;
        _emptyCart.TextAlign = ContentAlignment.MiddleCenter;
        _emptyCart.ForeColor = Ui.Muted;
        cartBody.Controls.Add(_cart);
        cartBody.Controls.Add(_emptyCart);
        _emptyCart.BringToFront();
        panel.Controls.Add(cartBody, 0, 1);
        var editing = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 4, 0, 4) };
        foreach (var button in new[] { _decrease, _increase, _edit, _remove }) button.Width = 95;
        editing.Controls.AddRange([_decrease, _increase, _edit, _remove]);
        panel.Controls.Add(editing, 0, 2);
        var promo = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
        promo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        promo.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        promo.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        _applyPromo.Dock = DockStyle.Fill;
        _removePromo.Dock = DockStyle.Fill;
        promo.Controls.Add(_promoCode, 0, 0);
        promo.Controls.Add(_applyPromo, 1, 0);
        promo.Controls.Add(_removePromo, 2, 0);
        panel.Controls.Add(promo, 0, 3);
        _promoStatus.ForeColor = Ui.Muted;
        _promoStatus.Margin = new Padding(4, 2, 4, 2);
        Ui.WrapToParent(_promoStatus, panel);
        panel.Controls.Add(_promoStatus, 0, 4);
        var totals = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 2, 0, 2) };
        totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        totals.Controls.Add(Ui.Label("Tạm tính"), 0, 0);
        totals.Controls.Add(_subtotal, 1, 0);
        totals.Controls.Add(Ui.Label("Giảm giá"), 0, 1);
        totals.Controls.Add(_discount, 1, 1);
        totals.Controls.Add(Ui.Label("Cần thanh toán", 12, true), 0, 2);
        _payable.ForeColor = Ui.Accent;
        totals.Controls.Add(_payable, 1, 2);
        foreach (Control value in totals.Controls) value.Margin = new Padding(4, 3, 4, 3);
        foreach (var value in new[] { _subtotal, _discount, _payable })
        {
            value.Dock = DockStyle.Fill;
            value.TextAlign = ContentAlignment.MiddleRight;
        }
        panel.Controls.Add(totals, 0, 5);
        var paymentActions = Ui.Rows(SizeType.AutoSize, SizeType.AutoSize);
        paymentActions.AutoSize = true;
        paymentActions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        paymentActions.Dock = DockStyle.Top;
        _checkout.Dock = DockStyle.Fill;
        _checkout.Height = 50;
        _unlock.Dock = DockStyle.Fill;
        paymentActions.Controls.Add(_checkout, 0, 0);
        paymentActions.Controls.Add(_unlock, 0, 1);
        panel.Controls.Add(paymentActions, 0, 6);
        return panel;
    }

    private async Task LoadAsync()
    {
        try
        {
            await _controller.InitializeAsync(_lifetime.Token);
            if (_closed || IsDisposed || Disposing) return;
            _loaded = true;
            _session.Text = $"Thu ngân: {_controller.Data.Session.StaffName}  |  Ca: {_controller.Data.Session.ShiftId}" +
                (_controller.Data.Session.IsDemo ? "  |  CHẾ ĐỘ DỮ LIỆU MẪU" : "");
            PopulateCategories();
            RefreshCart();
            _status.Text = _controller.PendingReceipt is null ?
                (_controller.CanEdit
                    ? "Sẵn sàng. Chọn món để thêm vào giỏ. F4: tìm món  •  F9: thanh toán" +
                        (_controller.Data.Session.IsDemo ? "  •  Mã mẫu: SALE10, GIAM5K" : "")
                    : "Phiên thu ngân chưa sẵn sàng. Kiểm tra ca/quyền bán, rồi chọn Tải lại thực đơn.") : _controller.Notice;
        }
        catch (OperationCanceledException) when (_closed) { return; }
        catch (Exception e)
        {
            if (_closed || IsDisposed || Disposing) return;
            _status.Text = "Không tải được dữ liệu: " + e.Message;
            Ui.Error(this, e);
        }
        SetActionState();
    }
    private void PopulateCategories()
    {
        var selected = _category.SelectedIndex > 0 ? _category.SelectedItem?.ToString() : null;
        _populatingCategories = true;
        _category.Items.Clear();
        _category.Items.Add("Tất cả danh mục");
        foreach (var category in _controller.Data.Menu.Select(x => x.Category).Distinct()) _category.Items.Add(category);
        _category.SelectedIndex = selected is not null && _category.Items.Contains(selected) ? _category.Items.IndexOf(selected) : 0;
        _populatingCategories = false;
        RenderProducts();
    }
    private void RenderProducts()
    {
        if (!_loaded) return;
        _renderedMenu = _controller.Data.Menu;
        var scroll = -_products.AutoScrollPosition.Y;
        _products.SuspendLayout();
        foreach (Control child in _products.Controls.Cast<Control>().ToArray()) child.Dispose();
        _products.Controls.Clear();
        var key = CartRules.SearchKey(_search.Text.Trim());
        var category = _category.SelectedIndex <= 0 ? null : _category.SelectedItem?.ToString();
        var items = _controller.Data.Menu.Where(x =>
            (category is null || x.Category == category) && CartRules.SearchKey(x.Name).Contains(key)).ToArray();
        foreach (var item in items)
        {
            var button = new ProductCard(item, () => AddItem(item)) { Tag = item };
            button.Enabled = item.Available && _controller.CanEdit && !_busy;
            _products.Controls.Add(button);
        }
        _menuCount.Text = $"{items.Length} món";
        if (items.Length == 0) _products.Controls.Add(Ui.Label("Không tìm thấy món phù hợp.\nThử tên khác hoặc chọn tất cả danh mục."));
        ResizeProductCards();
        _products.ResumeLayout();
        _products.AutoScrollPosition = new Point(0, scroll);
    }
    private void ResizeProductCards()
    {
        var space = Math.Max(1, _products.ClientSize.Width - _products.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
        var columns = Math.Max(1, space / Math.Max(1, (int)Math.Round(180 * DeviceDpi / 96d)));
        foreach (var card in _products.Controls.OfType<ProductCard>())
            card.Width = Math.Max(1, space / columns - card.Margin.Horizontal);
    }
    private CartLine? SelectedLine => _cart.SelectedRows.Count > 0 && _cart.SelectedRows[0].Tag is Guid id ?
        _controller.Lines.FirstOrDefault(x => x.LineId == id) : null;

    private void RefreshCart(Guid? selected = null)
    {
        if (!_loaded) return;
        // Checkout may re-read Manager's catalog. Refresh cards only when that
        // snapshot changes; ordinary cart edits keep their images and scroll.
        if (!_renderedMenu.Equals(_controller.Data.Menu)) PopulateCategories();
        _session.Text = $"Thu ngân: {_controller.Data.Session.StaffName}  |  Ca: {_controller.Data.Session.ShiftId}" +
            (!_controller.Data.Session.ShiftOpen ? " (đã đóng)" : "") +
            (_controller.Data.Session.IsDemo ? "  |  CHẾ ĐỘ DỮ LIỆU MẪU" : "");
        selected ??= SelectedLine?.LineId;
        _rendering = true;
        _cart.Rows.Clear();
        foreach (var line in _controller.Lines)
        {
            var row = _cart.Rows[_cart.Rows.Add(line.ProductName + (line.Options.Length == 0 ? "" : "\n" + line.OptionText),
                line.Quantity, CartRules.Money(line.UnitPrice), CartRules.Money(line.LineTotal))];
            row.Tag = line.LineId;
            row.MinimumHeight = 55;
        }
        _cart.ClearSelection();
        foreach (DataGridViewRow row in _cart.Rows)
            if (row.Tag is Guid id && id == selected) { row.Selected = true; _cart.CurrentCell = row.Cells[0]; break; }
        _rendering = false;
        _subtotal.Text = CartRules.Money(_controller.Totals.Subtotal);
        _discount.Text = CartRules.Money(_controller.Totals.Discount);
        _payable.Text = CartRules.Money(_controller.Totals.Payable);
        _cartCount.Text = $"{_controller.Lines.Sum(x => x.Quantity)} món / {_controller.Lines.Count} dòng";
        _emptyCart.Visible = _controller.Lines.Count == 0;
        _promoStatus.Text = _controller.Promotion is null ? "Chưa áp dụng khuyến mãi" :
            "Đang áp dụng: " + _controller.Promotion.Code + " — " + _controller.Promotion.Description;
        _promoCode.Text = _controller.Promotion?.Code ?? "";
        _promoStatus.ForeColor = Ui.Muted;
        _status.Text = !string.IsNullOrEmpty(_controller.Notice) ? _controller.Notice :
            _controller.State == CheckoutState.Paid ? "Đã thanh toán và lưu hóa đơn. Chọn Đơn mới (F2) để bán tiếp." :
            "Chọn món để thêm vào giỏ. F2: đơn mới  •  F4: tìm món  •  F9: thanh toán";
        SetActionState();
    }

    private void SetActionState()
    {
        var editable = _loaded && !_busy && _controller.CanEdit;
        _newOrder.Enabled = _loaded && !_busy && _controller.State is CheckoutState.Ready or CheckoutState.Paid;
        // Reload must remain available so a restored shift/permission can be read.
        _refresh.Enabled = _loaded && !_busy && _controller.State == CheckoutState.Ready;
        _history.Enabled = _loaded && _historySource is not null && !_busy;
        // Allow Click/F9 on a ready empty cart so it explains what is missing.
        _checkout.Enabled = _loaded && !_busy && _controller.State != CheckoutState.Saving &&
            (_controller.State == CheckoutState.Ready ? _controller.CanEdit : _controller.Lines.Count > 0);
        _checkout.Text = !_loaded || _controller.State == CheckoutState.Ready ? "Thanh toán  •  F9" :
            _controller.State == CheckoutState.Paid ? "Xem hóa đơn đã lưu" : "Xử lý giao dịch đang chờ";
        _applyPromo.Enabled = editable && _controller.Lines.Count > 0;
        _removePromo.Enabled = editable && _controller.Promotion is not null;
        _promoCode.Enabled = editable;
        var selected = editable && SelectedLine is not null;
        _increase.Enabled = _decrease.Enabled = _edit.Enabled = _remove.Enabled = selected;
        _unlock.Visible = _loaded && _controller.CanUnlockUnpaidOrder;
        _unlock.Enabled = !_busy;
        _search.Enabled = _category.Enabled = editable;
        foreach (var card in _products.Controls.OfType<ProductCard>())
            card.Enabled = editable && card.Tag is MenuItem { Available: true };
    }

    private void ApplyPromotion()
    {
        try { _controller.ApplyPromotion(_promoCode.Text); RefreshCart(); }
        catch (Exception e)
        {
            _promoStatus.Text = e.Message + (_controller.Promotion is { } active ? $" Đang giữ mã {active.Code}." : "");
            _promoStatus.ForeColor = Ui.Warning;
            _status.Text = e.Message;
            _promoCode.Focus();
            _promoCode.SelectAll();
        }
    }

    private void AddItem(MenuItem item)
    {
        if (!_controller.CanEdit || _busy) return;
        try
        {
            if (item.OptionGroups.Length == 0)
                RefreshCart(_controller.AddItemToCart(item.Id, 1, []).LineId);
            else
            {
                using var dialog = new OptionsForm(item);
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    RefreshCart(_controller.AddItemToCart(item.Id, dialog.Quantity, dialog.Selections).LineId);
            }
        }
        catch (Exception e) { Ui.Error(this, e); }
    }
    private void EditSelected()
    {
        if (!_controller.CanEdit || _busy || SelectedLine is not { } line) return;
        try
        {
            using var dialog = new OptionsForm(_controller.FindProduct(line.ProductId), line);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _controller.UpdateCartItem(line.LineId, dialog.Quantity, dialog.Selections);
                RefreshCart(line.LineId);
            }
        }
        catch (Exception e) { Ui.Error(this, e); }
    }
    private void AdjustQuantity(int delta)
    {
        if (SelectedLine is { } line) Run(() => _controller.ChangeQuantity(line.LineId, line.Quantity + delta));
    }
    private void DeleteSelected()
    {
        if (SelectedLine is not { } line || !_controller.CanEdit || _busy) return;
        if (MessageBox.Show(this, $"Xóa {line.ProductName} khỏi giỏ?", "Xác nhận",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) Run(() => _controller.RemoveCartItem(line.LineId));
    }
    private void NewOrder()
    {
        if (!_newOrder.Enabled) return;
        var confirmed = _controller.Lines.Count == 0 || _controller.State == CheckoutState.Paid ||
            MessageBox.Show(this, "Bỏ giỏ chưa thanh toán và tạo đơn mới?", "Xác nhận",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        if (confirmed) Run(() => _controller.StartNewOrder(true));
    }
    private void Run(Action action)
    {
        try { action(); RefreshCart(); }
        catch (Exception e) { _status.Text = e.Message; Ui.Error(this, e); }
    }
    private async Task ReloadAsync()
    {
        _busy = true;
        SetActionState();
        try { await _controller.RefreshCatalogAsync(); PopulateCategories(); RefreshCart(); }
        catch (Exception e) { Ui.Error(this, e); }
        finally { _busy = false; SetActionState(); RenderProducts(); }
    }
    private void OpenPayment()
    {
        if (!_checkout.Enabled) return;
        if (_controller.PaidReceipt is { } paid) { ShowReceipt(paid); return; }
        if (_controller.Lines.Count == 0)
        {
            const string message = "Vui lòng chọn ít nhất một món trước khi thanh toán.";
            _status.Text = message;
            MessageBox.Show(this, message, "Giỏ hàng trống", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _search.Focus();
            return;
        }
        using var payment = new PaymentForm(_controller);
        payment.ShowDialog(this);
        RefreshCart();
        if (payment.CompletedReceipt is { } receipt)
        {
            ShowReceipt(receipt);
            // Keep the paid cart visible; only an explicit New order clears it.
            RefreshCart();
        }
    }
    private async Task OpenHistoryAsync()
    {
        if (!_loaded || _historySource is null || _busy) return;
        var canCreate = _controller.State is (CheckoutState.Ready or CheckoutState.Paid) &&
            _controller.Data.Session.CanSell && _controller.Data.Session.ShiftOpen;
        using var history = new ReceiptHistoryForm(_historySource, canCreate);
        if (history.ShowDialog(this) != DialogResult.OK || history.SelectedReceipt is not { } receipt) return;
        var confirmed = _controller.State == CheckoutState.Paid || _controller.Lines.Count == 0 ||
            MessageBox.Show(this, "Giỏ đang có món chưa thanh toán. Thay bằng đơn tương tự từ hóa đơn đã chọn?",
                "Xác nhận thay giỏ", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        if (!confirmed) return;
        _busy = true;
        SetActionState();
        RenderProducts();
        try
        {
            await _controller.CreateSimilarOrderAsync(receipt, confirmed);
            PopulateCategories();
            RefreshCart(_controller.Lines.FirstOrDefault()?.LineId);
        }
        catch (Exception e) { Ui.Error(this, e); RefreshCart(); }
        finally { _busy = false; SetActionState(); RenderProducts(); }
    }

    private void ShowReceipt(Receipt receipt)
    {
        using var form = new ReceiptForm(receipt);
        form.ShowDialog(this);
    }
    private async Task UnlockAsync()
    {
        if (MessageBox.Show(this, "Chỉ mở lại khi kiểm tra xác nhận chưa lưu. Nếu đã nhận tiền, cần xử lý khoản đã nhận trước khi đổi đơn. Tiếp tục?",
            "Mở giỏ chưa lưu", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _busy = true;
        SetActionState();
        try { await _controller.UnlockUnpaidOrderAsync(); RefreshCart(); }
        catch (Exception e) { Ui.Error(this, e); RefreshCart(); }
        finally { _busy = false; SetActionState(); RenderProducts(); }
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _closed = true;
        if (!_resourcesDisposed) _lifetime.Cancel();
        _windowLease?.Dispose();
        base.OnFormClosed(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        {
            _resourcesDisposed = true;
            _closed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
            _windowLease?.Dispose();
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
