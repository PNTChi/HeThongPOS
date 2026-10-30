using Cashier.Core;

namespace Cashier.WinForms;

public sealed class OptionsForm : Form
{
    private readonly MenuItem _item;
    private readonly List<(Control Control, OptionSelection Selection)> _choices = [];
    private readonly NumericUpDown _quantity = new() { Minimum = 1, Maximum = CartRules.MaximumQuantity, Width = 100 };
    private readonly Label _price = Ui.Label("", 12, true);
    private readonly Label _error = Ui.Label();
    public int Quantity => (int)_quantity.Value;
    public IReadOnlyList<OptionSelection> Selections => _choices.Where(x => IsChecked(x.Control)).Select(x => x.Selection).ToArray();

    public OptionsForm(MenuItem item, CartLine? existing = null)
    {
        _item = item;
        Ui.FormStyle(this, existing is null ? "Tùy chọn món" : "Sửa món trong giỏ", new Size(610, 680));
        MinimumSize = new Size(530, 550);
        MaximizeBox = false;
        var root = Ui.Rows(SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize);
        root.Padding = new Padding(20);
        root.Controls.Add(Ui.Label(item.Name, 18, true), 0, 0);
        var groups = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(0, 8, 8, 8)
        };
        root.Controls.Add(groups, 0, 1);
        foreach (var group in item.OptionGroups)
        {
            var box = new GroupBox
            {
                Text = group.Name, Width = 540,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12), Margin = new Padding(0, 6, 0, 10)
            };
            var stack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
                Dock = DockStyle.Fill, Padding = new Padding(3, 10, 3, 3)
            };
            box.Controls.Add(stack);
            // A separate rule line prevents long captions from covering the first choice.
            var rule = Ui.Label(group.Minimum > 0
                ? (group.Minimum == group.Maximum
                    ? $"Bắt buộc — chọn {group.Minimum}"
                    : $"Bắt buộc — chọn từ {group.Minimum} đến {group.Maximum}")
                : $"Không bắt buộc — tối đa {group.Maximum}");
            rule.ForeColor = Ui.Muted;
            rule.Margin = new Padding(6, 0, 6, 8);
            stack.Controls.Add(rule);
            foreach (var choice in group.Choices)
            {
                var label = choice.Name + (choice.ExtraPrice == 0 ? " (không phụ thu)" : " (+" + CartRules.Money(choice.ExtraPrice) + ")");
                if (!choice.Available) label += " — Tạm ngừng";
                var selection = new OptionSelection(group.Id, choice.Id);
                Control control;
                if (group.Maximum == 1)
                {
                    var radio = new RadioButton { Text = label, AutoSize = true, Enabled = choice.Available, Margin = new Padding(6) };
                    radio.CheckedChanged += (_, _) => UpdatePrice();
                    control = radio;
                }
                else
                {
                    var check = new CheckBox { Text = label, AutoSize = true, Enabled = choice.Available, Margin = new Padding(6) };
                    check.CheckedChanged += (_, _) => UpdatePrice();
                    control = check;
                }
                stack.Controls.Add(control);
                _choices.Add((control, selection));
                var wasSelected = existing?.Options.Any(x => x.GroupId == group.Id && x.ChoiceId == choice.Id) ?? false;
                // An old unavailable topping must not remain checked and disabled:
                // the cashier needs to be able to correct the existing cart line.
                if (wasSelected && choice.Available) SetChecked(control, true);
            }
            if (group.Maximum == 1 && group.Minimum == 0)
            {
                var none = new RadioButton { Text = "Không thêm", AutoSize = true, Margin = new Padding(6) };
                stack.Controls.Add(none);
                none.CheckedChanged += (_, _) => UpdatePrice();
                none.Checked = !_choices.Any(x => x.Selection.GroupId == group.Id && IsChecked(x.Control));
            }
            else if (existing is null && group.Minimum == 1 && group.Maximum == 1)
            {
                var first = _choices.FirstOrDefault(x => x.Selection.GroupId == group.Id && x.Control.Enabled);
                if (first.Control is not null) SetChecked(first.Control, true);
            }
            groups.Controls.Add(box);
        }
        if (item.OptionGroups.Length == 0) groups.Controls.Add(Ui.Label("Món này không có tùy chọn."));
        void ResizeGroups()
        {
            var width = Math.Max(1, groups.ClientSize.Width - groups.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
            foreach (var box in groups.Controls.OfType<GroupBox>())
            {
                var boxWidth = Math.Max(1, width - box.Margin.Horizontal);
                box.MinimumSize = new Size(boxWidth, 0);
                box.MaximumSize = new Size(boxWidth, 0);
                box.Width = boxWidth;
                foreach (var stack in box.Controls.OfType<FlowLayoutPanel>())
                    foreach (Control choice in stack.Controls)
                        choice.MaximumSize = new Size(Math.Max(1, box.Width - box.Padding.Horizontal - stack.Padding.Horizontal - choice.Margin.Horizontal - 12), 0);
            }
        }
        groups.SizeChanged += (_, _) => ResizeGroups();
        var amount = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 8) };
        amount.Controls.Add(Ui.Label("Số lượng:"));
        amount.Controls.Add(_quantity);
        amount.Controls.Add(_price);
        root.Controls.Add(amount, 0, 2);
        _error.ForeColor = Ui.Warning;
        if (existing is not null && existing.Options.Any(old => !item.OptionGroups.Any(group =>
            group.Id == old.GroupId && group.Choices.Any(choice => choice.Id == old.ChoiceId && choice.Available))))
            _error.Text = "Một số tùy chọn cũ đã ngừng bán hoặc bị gỡ. Kiểm tra lại lựa chọn và thành tiền trước khi lưu.";
        Ui.WrapToParent(_error, root);
        root.Controls.Add(_error, 0, 3);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = Ui.Button(existing is null ? "Thêm vào giỏ" : "Lưu thay đổi", true);
        save.Width = 160;
        save.Click += (_, _) => Save();
        var cancel = Ui.Button("Hủy");
        cancel.Width = 100;
        cancel.DialogResult = DialogResult.Cancel;
        actions.Controls.Add(save);
        actions.Controls.Add(cancel);
        root.Controls.Add(actions, 0, 4);
        Controls.Add(root);
        AcceptButton = save;
        CancelButton = cancel;
        _quantity.Value = existing?.Quantity ?? 1;
        _quantity.ValueChanged += (_, _) => UpdatePrice();
        UpdatePrice();
        ResizeGroups();
    }

    private void Save()
    {
        try
        {
            if (decimal.Truncate(_quantity.Value) != _quantity.Value)
                throw new BusinessException("Số lượng phải là số nguyên.");
            CartRules.ValidateQuantity(Quantity);
            CartRules.ApplyOption(_item, Selections);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception e) { _error.Text = e.Message; }
    }
    private void UpdatePrice()
    {
        var extras = _choices.Where(x => IsChecked(x.Control)).Sum(x =>
            _item.OptionGroups.First(g => g.Id == x.Selection.GroupId).Choices.First(c => c.Id == x.Selection.ChoiceId).ExtraPrice);
        _price.Text = "Thành tiền: " + CartRules.Money((_item.BasePrice + extras) * _quantity.Value);
    }
    private static bool IsChecked(Control c) => c is RadioButton r ? r.Checked : c is CheckBox b && b.Checked;
    private static void SetChecked(Control c, bool value)
    {
        if (c is RadioButton r) r.Checked = value;
        if (c is CheckBox b) b.Checked = value;
    }
}
