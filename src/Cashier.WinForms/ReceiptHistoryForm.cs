using Cashier.Core;

namespace Cashier.WinForms;

public sealed class ReceiptHistoryForm : Form
{
    private readonly IReceiptHistory _source;
    private readonly bool _canCreate;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DataGridView _list = new();
    private readonly TextBox _search = new() { Dock = DockStyle.Fill, PlaceholderText = "Tìm mã hóa đơn, tên món hoặc thu ngân…" };
    private readonly ReceiptView _detail = new();
    private readonly Label _status = Ui.Label("Đang đọc hóa đơn đã lưu…");
    private readonly Button _create = Ui.Button("Tạo đơn tương tự", true);
    private readonly Button _view = Ui.Button("Xem / in hóa đơn");
    private readonly Button _reload = Ui.Button("Tải lại");
    private IReadOnlyList<Receipt> _receipts = Array.Empty<Receipt>();
    private Receipt? _selected;
    private bool _loading;
    public Receipt? SelectedReceipt { get; private set; }

    public ReceiptHistoryForm(IReceiptHistory source, bool canCreate)
    {
        _source = source;
        _canCreate = canCreate;
        SuspendLayout();
        Ui.FormStyle(this, "Lịch sử hóa đơn — Cashier", new Size(1120, 700));
        AutoScaleDimensions = new SizeF(96, 96);
        MinimumSize = new Size(960, 580);
        var root = Ui.Rows(SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize,
            SizeType.Percent, SizeType.AutoSize, SizeType.AutoSize);
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Padding = new Padding(16);
        root.Controls.Add(Ui.Label("Lịch sử hóa đơn", 18, true), 0, 0);
        var note = Ui.Label("Hóa đơn đã lưu tại quầy. Đơn tương tự dùng giá hiện tại; hãy kiểm tra món và ưu đãi trước khi thanh toán.");
        note.ForeColor = Ui.Muted;
        note.Dock = DockStyle.Fill;
        root.Controls.Add(note, 0, 1);
        root.Controls.Add(_search, 0, 2);
        var middle = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        middle.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        ConfigureList();
        middle.Controls.Add(_list, 0, 0);
        middle.Controls.Add(_detail, 1, 0);
        root.Controls.Add(middle, 0, 3);
        _status.Dock = DockStyle.Fill;
        root.Controls.Add(_status, 0, 4);
        // Keep wrapped labels inside the actual column after resizing/DPI scaling.
        void ResizeText()
        {
            var width = Math.Max(1, root.ClientSize.Width - root.Padding.Horizontal);
            note.MaximumSize = new Size(Math.Max(1, width - note.Margin.Horizontal), 0);
            _status.MaximumSize = new Size(Math.Max(1, width - _status.Margin.Horizontal), 0);
        }
        root.SizeChanged += (_, _) => ResizeText();
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft
        };
        _create.Width = 190;
        _view.Width = 180;
        _reload.Width = 110;
        var close = Ui.Button("Đóng");
        close.Width = 90;
        close.DialogResult = DialogResult.Cancel;
        actions.Controls.AddRange([_create, _view, _reload, close]);
        root.Controls.Add(actions, 0, 5);
        Controls.Add(root);
        CancelButton = close;
        _create.Click += (_, _) =>
        {
            if (!_canCreate || _loading || _selected is null) return;
            SelectedReceipt = _selected;
            DialogResult = DialogResult.OK;
            Close();
        };
        _view.Click += (_, _) => ViewSelected();
        _reload.Click += async (_, _) => await LoadHistoryAsync();
        _search.TextChanged += (_, _) => RenderReceipts();
        _list.SelectionChanged += (_, _) => SelectReceipt();
        _list.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ViewSelected(); };
        Shown += async (_, _) => await LoadHistoryAsync();
        SetActions();
        ResumeLayout(true);
        ResizeText();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Apply after automatic DPI scaling, so the initial dialog fits the monitor.
        var area = Screen.FromControl(Owner ?? this).WorkingArea;
        var gap = Math.Max(8, (int)Math.Round(16 * DeviceDpi / 96d));
        var available = new Size(Math.Max(1, area.Width - gap * 2), Math.Max(1, area.Height - gap * 2));
        MinimumSize = new Size(Math.Min(MinimumSize.Width, available.Width),
            Math.Min(MinimumSize.Height, available.Height));
        var width = Math.Min(Width, available.Width);
        var height = Math.Min(Height, available.Height);
        var center = Owner is { WindowState: not FormWindowState.Minimized } ? Owner.Bounds : area;
        var left = Math.Clamp(center.Left + (center.Width - width) / 2,
            area.Left + gap, area.Right - gap - width);
        var top = Math.Clamp(center.Top + (center.Height - height) / 2,
            area.Top + gap, area.Bottom - gap - height);
        Bounds = new Rectangle(left, top, width, height);
    }

    private void ConfigureList()
    {
        _list.Dock = DockStyle.Fill;
        _list.ReadOnly = true;
        _list.AllowUserToAddRows = false;
        _list.AllowUserToDeleteRows = false;
        _list.AllowUserToResizeRows = false;
        _list.MultiSelect = false;
        _list.RowHeadersVisible = false;
        _list.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _list.BackgroundColor = Ui.Surface;
        _list.GridColor = Ui.Border;
        _list.BorderStyle = BorderStyle.None;
        _list.EnableHeadersVisualStyles = false;
        _list.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _list.ColumnHeadersDefaultCellStyle.BackColor = Ui.Soft;
        _list.ColumnHeadersDefaultCellStyle.ForeColor = Ui.Brand;
        _list.ColumnHeadersDefaultCellStyle.SelectionBackColor = Ui.Soft;
        _list.ColumnHeadersDefaultCellStyle.SelectionForeColor = Ui.Brand;
        _list.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        _list.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 8, 6, 8);
        _list.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _list.DefaultCellStyle.SelectionBackColor = Ui.Selection;
        _list.DefaultCellStyle.SelectionForeColor = Ui.Ink;
        _list.DefaultCellStyle.Padding = new Padding(4);
        _list.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _list.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _list.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Time", HeaderText = "Ngày / giờ", Width = 150, MinimumWidth = 120,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _list.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Number", HeaderText = "Hóa đơn", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 160, SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _list.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Payable", HeaderText = "Thanh toán", Width = 125, MinimumWidth = 110,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _list.Columns[2].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
    }

    private async Task LoadHistoryAsync()
    {
        if (_loading) return;
        _loading = true;
        _status.Text = "Đang đọc hóa đơn đã lưu…";
        SetActions();
        try
        {
            var result = await _source.ReadRecentAsync(100, _lifetime.Token);
            if (IsDisposed || Disposing) return;
            _receipts = result.Receipts;
            RenderReceipts();
            _status.ForeColor = result.UnreadableFiles > 0 ? Ui.Warning : Ui.Muted;
            _status.Text = $"Đã tải {_receipts.Count} hóa đơn (tối đa 100, mới nhất trước)." +
                (result.UnreadableFiles > 0
                    ? $" Bỏ qua {result.UnreadableFiles} tệp lỗi/không đọc được; không xóa các tệp đó."
                    : "") +
                (!_canCreate ? " Chỉ xem/in; phiên hiện tại không cho tạo đơn mới." : "");
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (IsDisposed || Disposing) return;
            _receipts = Array.Empty<Receipt>();
            RenderReceipts();
            _status.ForeColor = Ui.Warning;
            _status.Text = "Không đọc được lịch sử: " + e.Message;
        }
        finally
        {
            _loading = false;
            if (!IsDisposed && !Disposing) SetActions();
        }
    }

    private void RenderReceipts()
    {
        if (IsDisposed || Disposing) return;
        var key = CartRules.SearchKey(_search.Text.Trim());
        var previousId = _selected?.OrderId;
        _list.Rows.Clear();
        foreach (var receipt in _receipts.Where(x => CartRules.SearchKey(
            x.ReceiptNumber + " " + x.StaffName + " " +
            string.Join(" ", x.Lines.Select(line => line.ProductName))).Contains(key)))
        {
            var row = _list.Rows[_list.Rows.Add(receipt.PaidAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                receipt.ReceiptNumber, CartRules.Money(receipt.Totals.Payable))];
            row.Tag = receipt;
            row.Cells[1].ToolTipText = receipt.ReceiptNumber;
            row.MinimumHeight = Math.Max(58, (int)Math.Ceiling(58 * DeviceDpi / 96d));
        }
        _list.ClearSelection();
        var select = _list.Rows.Cast<DataGridViewRow>().FirstOrDefault(x =>
            x.Tag is Receipt receipt && receipt.OrderId == previousId) ??
            _list.Rows.Cast<DataGridViewRow>().FirstOrDefault();
        if (select is not null) { select.Selected = true; _list.CurrentCell = select.Cells[0]; }
        SelectReceipt();
    }

    private void SelectReceipt()
    {
        _selected = _list.SelectedRows.Count > 0 ? _list.SelectedRows[0].Tag as Receipt : null;
        _detail.ShowReceipt(_selected);
        SetActions();
    }

    private void SetActions()
    {
        _create.Enabled = !_loading && _canCreate && _selected is not null;
        _view.Enabled = !_loading && _selected is not null;
        _reload.Enabled = !_loading;
        _search.Enabled = !_loading;
    }

    private void ViewSelected()
    {
        if (_loading || _selected is null) return;
        using var receipt = new ReceiptForm(_selected);
        receipt.ShowDialog(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed) { _lifetime.Cancel(); _lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
