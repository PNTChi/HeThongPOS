namespace Cashier.WinForms;

internal static class Ui
{
    // Central palette: change these values to retheme later.
    public static readonly Color Brand = Color.FromArgb(91, 58, 41);
    public static readonly Color Accent = Color.FromArgb(181, 83, 45);
    public static readonly Color Background = Color.FromArgb(246, 241, 234);
    public static readonly Color Surface = Color.White;
    public static readonly Color CardBackground = Color.FromArgb(255, 249, 242);
    public static readonly Color Soft = Color.FromArgb(241, 230, 217);
    public static readonly Color Selection = Color.FromArgb(234, 212, 190);
    public static readonly Color Border = Color.FromArgb(221, 203, 187);
    public static readonly Color Ink = Color.FromArgb(62, 42, 32);
    public static readonly Color Muted = Color.FromArgb(120, 104, 93);
    public static readonly Color Warning = Color.Firebrick;
    public static Button Button(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text, AutoSize = false, Height = 40, MinimumSize = new Size(70, 36),
            FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Surface,
            ForeColor = primary ? Color.White : Brand, Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 10, primary ? FontStyle.Bold : FontStyle.Regular),
            Margin = new Padding(4), UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(157, 68, 36) : Soft;
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(136, 55, 29) : Selection;
        return button;
    }
    public static Label Label(string text = "", float size = 10, bool bold = false) => new()
    {
        Text = text, AutoSize = true, ForeColor = Ink,
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        Margin = new Padding(4, 6, 4, 6)
    };
    public static void FormStyle(Form form, string title, Size size)
    {
        form.Text = title;
        form.Font = new Font("Segoe UI", 10);
        form.BackColor = Background;
        form.ForeColor = Ink;
        form.StartPosition = FormStartPosition.CenterParent;
        form.ClientSize = size;
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.AutoScaleDimensions = new SizeF(96, 96);
        form.Load += (_, _) => FitToScreen(form);
    }
    private static void FitToScreen(Form form)
    {
        var area = Screen.FromControl(form.Owner ?? form).WorkingArea;
        var gap = Math.Max(8, (int)Math.Round(12 * form.DeviceDpi / 96d));
        var available = new Size(Math.Max(1, area.Width - gap * 2), Math.Max(1, area.Height - gap * 2));
        form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, available.Width),
            Math.Min(form.MinimumSize.Height, available.Height));
        form.Size = new Size(Math.Min(form.Width, available.Width), Math.Min(form.Height, available.Height));
    }
    public static void WrapToParent(Label label, Control parent)
    {
        void ResizeText() => label.MaximumSize = new Size(Math.Max(1,
            parent.ClientSize.Width - parent.Padding.Horizontal - label.Margin.Horizontal), 0);
        parent.SizeChanged += (_, _) => ResizeText();
        ResizeText();
    }
    public static TableLayoutPanel Rows(params SizeType[] types)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = types.Length };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var type in types) panel.RowStyles.Add(new RowStyle(type, type == SizeType.Percent ? 100 : 0));
        return panel;
    }
    public static void Error(IWin32Window parent, Exception exception) =>
        MessageBox.Show(parent, exception.Message, "Không thể thực hiện", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
