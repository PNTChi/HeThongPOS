using Manager.Core;

namespace Manager.WinForms;

public sealed class LoginForm : Form
{
    private readonly ManagerController _controller;
    private readonly TextBox _txtUsername;
    private readonly TextBox _txtPassword;
    private readonly Label _lblError;
    private readonly Button _btnLogin;

    public StaffAccount? AuthenticatedUser { get; private set; }

    public LoginForm(ManagerController controller)
    {
        _controller = controller;

        Text = "POS - Đăng nhập";
        Font = new Font("Segoe UI", 10);
        BackColor = ManagerUi.Background;
        ForeColor = ManagerUi.Ink;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(460, 420);
        AutoScaleMode = AutoScaleMode.Dpi;

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(32, 24, 32, 24)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); // Header
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); // Subtitle
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70)); // Username
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70)); // Password
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 55)); // Button
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Error & Demo Info

        var lblTitle = new Label
        {
            Text = "POS - ĐĂNG NHẬP",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = ManagerUi.Primary
        };

        var lblSub = new Label
        {
            Text = "Truy cập hệ thống bằng tài khoản được cấp",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = ManagerUi.Muted
        };

        var userGroup = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        userGroup.Controls.Add(ManagerUi.Label("Tài khoản:", 9.5f, true));
        _txtUsername = new TextBox
        {
            Width = 390,
            Height = 32,
            Font = new Font("Segoe UI", 11),
            Text = "admin"
        };
        userGroup.Controls.Add(_txtUsername);

        var passGroup = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        passGroup.Controls.Add(ManagerUi.Label("Mật khẩu:", 9.5f, true));
        _txtPassword = new TextBox
        {
            Width = 390,
            Height = 32,
            Font = new Font("Segoe UI", 11),
            UseSystemPasswordChar = true,
            Text = "123456"
        };
        passGroup.Controls.Add(_txtPassword);

        _btnLogin = ManagerUi.Button("Đăng nhập", primary: true, height: 42);
        _btnLogin.AutoSize = false;
        _btnLogin.Dock = DockStyle.Fill;
        _btnLogin.Click += async (s, e) => await DoLoginAsync();

        var errorPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        _lblError = new Label
        {
            Text = "",
            ForeColor = ManagerUi.Danger,
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Italic),
            Margin = new Padding(0, 8, 0, 8)
        };
        errorPanel.Controls.Add(_lblError);

        var lblDemo = new Label
        {
            Text = "💡 Tài khoản mẫu:\n• admin / 123456 (Quản lý - Yến Nhi)\n• khoi / 123456 (Thu ngân - Tuấn Khôi)\n• chi / 123456 (Thu ngân - Thảo Chi)",
            ForeColor = ManagerUi.Muted,
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5f),
            Margin = new Padding(0, 4, 0, 0)
        };
        errorPanel.Controls.Add(lblDemo);

        panel.Controls.Add(lblTitle, 0, 0);
        panel.Controls.Add(lblSub, 0, 1);
        panel.Controls.Add(userGroup, 0, 2);
        panel.Controls.Add(passGroup, 0, 3);
        panel.Controls.Add(_btnLogin, 0, 4);
        panel.Controls.Add(errorPanel, 0, 5);

        Controls.Add(panel);
        AcceptButton = _btnLogin;
    }

    private async Task DoLoginAsync()
    {
        _lblError.Text = "";
        _btnLogin.Enabled = false;
        try
        {
            var user = await _controller.LoginAsync(_txtUsername.Text, _txtPassword.Text);
            AuthenticatedUser = user;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _lblError.Text = "⚠️ " + ex.Message;
        }
        finally
        {
            _btnLogin.Enabled = true;
        }
    }
}

