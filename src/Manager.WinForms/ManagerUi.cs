namespace Manager.WinForms;

internal static class ManagerUi
{
    public static readonly Color Primary = Color.FromArgb(26, 54, 93);       // Deep Navy
    public static readonly Color PrimaryHover = Color.FromArgb(43, 84, 138);
    public static readonly Color Accent = Color.FromArgb(196, 92, 42);         // Rust Orange
    public static readonly Color AccentHover = Color.FromArgb(218, 112, 60);
    public static readonly Color Background = Color.FromArgb(245, 247, 250);   // Modern Off-white
    public static readonly Color Surface = Color.White;
    public static readonly Color Card = Color.FromArgb(255, 255, 255);
    public static readonly Color Border = Color.FromArgb(226, 232, 240);
    public static readonly Color BorderFocus = Color.FromArgb(99, 102, 241);
    public static readonly Color Ink = Color.FromArgb(15, 23, 42);             // Dark Slate
    public static readonly Color Muted = Color.FromArgb(100, 116, 139);        // Slate 500
    public static readonly Color Success = Color.FromArgb(22, 101, 52);        // Green 800
    public static readonly Color SuccessBg = Color.FromArgb(240, 253, 244);
    public static readonly Color Danger = Color.FromArgb(185, 28, 28);          // Red 700
    public static readonly Color DangerBg = Color.FromArgb(254, 242, 242);
    public static readonly Color HeaderBg = Color.FromArgb(30, 41, 59);

    public static Button Button(string text, bool primary = false, bool isDanger = false, int height = 36)
    {
        var font = new Font("Segoe UI", 9.5f, (primary || isDanger) ? FontStyle.Bold : FontStyle.Regular);
        var textSize = TextRenderer.MeasureText(text, font);
        int computedWidth = Math.Max(84, textSize.Width + 28);

        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            Height = height,
            Width = computedWidth,
            MinimumSize = new Size(computedWidth, height),
            Padding = new Padding(12, 0, 12, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = isDanger ? Danger : (primary ? Primary : Surface),
            ForeColor = (primary || isDanger) ? Color.White : Ink,
            Cursor = Cursors.Hand,
            Font = font,
            Margin = new Padding(4),
            UseVisualStyleBackColor = false
        };
        btn.FlatAppearance.BorderColor = isDanger ? Danger : (primary ? Primary : Border);
        btn.FlatAppearance.MouseOverBackColor = isDanger ? Color.FromArgb(153, 27, 27) : (primary ? PrimaryHover : Color.FromArgb(241, 245, 249));
        btn.FlatAppearance.BorderSize = 1;
        return btn;
    }

    public static Label Label(string text = "", float size = 9.5f, bool bold = false, Color? color = null) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = color ?? Ink,
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        Margin = new Padding(3, 4, 3, 4)
    };

    public static Label FormLabel(string text, float size = 9.5f, bool bold = true) => new()
    {
        Text = text,
        AutoSize = false,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Ink,
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        Margin = new Padding(3, 4, 8, 4)
    };

    public static TextBox TextBox(string text = "", bool isPassword = false) => new()
    {
        Text = text,
        Font = new Font("Segoe UI", 10),
        Height = 32,
        UseSystemPasswordChar = isPassword,
        BorderStyle = BorderStyle.FixedSingle,
        Margin = new Padding(3, 3, 3, 3)
    };

    public static DataGridView CreateDataGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Surface,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = Color.FromArgb(241, 245, 249),
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoGenerateColumns = false,
            EnableHeadersVisualStyles = false,
            Font = new Font("Segoe UI", 9.5f)
        };

        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(241, 245, 249),
            ForeColor = Color.FromArgb(71, 85, 105),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(8, 6, 8, 6)
        };
        grid.ColumnHeadersHeight = 38;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface,
            ForeColor = Ink,
            SelectionBackColor = Color.FromArgb(224, 231, 255),
            SelectionForeColor = Color.FromArgb(30, 27, 75),
            Padding = new Padding(8, 4, 8, 4)
        };

        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(248, 250, 252),
            ForeColor = Ink,
            SelectionBackColor = Color.FromArgb(224, 231, 255),
            SelectionForeColor = Color.FromArgb(30, 27, 75)
        };

        grid.RowTemplate.Height = 34;
        return grid;
    }

    public static Panel CardPanel(Padding padding = default)
    {
        var p = new Panel
        {
            BackColor = Card,
            Padding = padding == default ? new Padding(12) : padding,
            Margin = new Padding(4)
        };
        p.Paint += (s, e) =>
        {
            using var pen = new Pen(Border, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
        };
        return p;
    }

    public static string FormatVnd(decimal amount) => $"{amount:N0} đ";

    public static void Info(IWin32Window parent, string message, string title = "Thông báo") =>
        MessageBox.Show(parent, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static void Warning(IWin32Window parent, string message, string title = "Cảnh báo") =>
        MessageBox.Show(parent, message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public static void Error(IWin32Window parent, Exception ex, string title = "Lỗi xử lý") =>
        MessageBox.Show(parent, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
}

