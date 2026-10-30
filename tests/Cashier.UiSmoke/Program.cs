using System.Collections.Immutable;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Cashier.Core;
using Cashier.Infrastructure;
using Cashier.WinForms;
using Manager.Core;
using Manager.WinForms;
using Product = Cashier.Core.MenuItem;

namespace Cashier.UiSmoke;

internal static class Program
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-15T10:00:00+07:00");
    private static readonly List<string> Results = [];
    private static int _passed;
    private static int _failed;
    private static string _output = "";

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length == 2 && args[0] == "--lease-probe")
        {
            try
            {
                var fixture = CreateCashier();
                using var form = new SalesForm(fixture.Controller, args[1]);
                return 0;
            }
            catch (BusinessException) { return 3; }
        }
        _output = args.Length > 0 ? Path.GetFullPath(args[0]) :
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../verification/ui"));
        Directory.CreateDirectory(_output);
        // A real STA message loop is required: async JSON loading creates WinForms
        // controls after awaiting. Merely pumping DoEvents without this context
        // would resume that code on a worker thread.
        using var dispatcher = new Control();
        _ = dispatcher.Handle;
        using var synchronization = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(synchronization);
        using var context = new ApplicationContext();
        dispatcher.BeginInvoke((Action)(() =>
        {
            try
            {
                var uiThread = Environment.CurrentManagedThreadId;
                Await(VerifyUiContinuation(uiThread));
                Log("WinForms runtime smoke; hidden controls only; no printers, dialogs or user AppData.");
                Run("Sales, search, promotion and options", SalesAndOptions);
                Run("Manager catalog refresh after checkout", CatalogRefreshAfterCheckout);
                Run("Cashier session recovery", SessionRecovery);
                Run("Correct unavailable options in an existing cart line", EditUnavailableOptions);
                Run("Close Cashier during initial loading", CloseDuringLoad);
                Run("One Cashier per checkout journal", SharedJournalLease);
                Run("Payment, receipt and history", PaymentAndHistory);
                Run("Shift reconciliation", ShiftReconciliation);
            }
            catch (Exception exception) { _failed++; Log("FAIL | Harness initialization | " + exception); }
            finally { Log($"RESULT: {_passed} passed, {_failed} failed."); context.ExitThread(); }
        }));
        Application.Run(context);
        return _failed == 0 ? 0 : 1;
    }

    private static async Task VerifyUiContinuation(int expectedThread)
    {
        await Task.Delay(15);
        if (Environment.CurrentManagedThreadId != expectedThread || Thread.CurrentThread.GetApartmentState() != ApartmentState.STA ||
            SynchronizationContext.Current is not WindowsFormsSynchronizationContext)
            throw new InvalidOperationException("An async continuation left the WinForms STA context; refusing to construct forms.");
        Check(true, "Asynchronous operations resume on the WinForms STA thread");
    }

    private static void SalesAndOptions()
    {
        var fixture = CreateCashier();
        using var sales = new SalesForm(fixture.Controller, Path.Combine(_output, "test-data"), fixture.Orders);
        Prepare(sales, new Size(1360, 800));
        Until(() => Field<bool>(sales, "_loaded") && Field<FlowLayoutPanel>(sales, "_products").Controls.Count > 0);
        var products = Field<FlowLayoutPanel>(sales, "_products");
        var search = Field<TextBox>(sales, "_search");
        var category = Field<ComboBox>(sales, "_category");
        Check(products.Controls.OfType<Button>().Count() == 10, "Catalog renders all 10 product cards");
        var soldOut = products.Controls.OfType<Button>().Single(x => x.Tag is Product { Id: "GA" });
        Check(!soldOut.Enabled, "Unavailable product cannot be selected");
        var retainedCards = products.Controls.OfType<Button>().ToArray();
        var plain = retainedCards.Single(x => x.Tag is Product { Id: "CA-PHE-DEN" });
        Click(plain);
        Click(plain);
        Check(fixture.Controller.Lines.Count == 1 && fixture.Controller.Lines[0].Quantity == 2,
            "Clicking a normal product twice updates one cart row");
        Check(retainedCards.SequenceEqual(products.Controls.OfType<Button>()) && retainedCards.All(x => !x.IsDisposed),
            "Editing the cart preserves existing card controls and images");
        var beforeSearch = fixture.Controller.Totals;
        search.Text = "khong-co-mon-nay-9876";
        Check(!products.Controls.OfType<Button>().Any() && products.Controls.OfType<Label>().Any(x => x.Text.Contains("Không tìm thấy")),
            "An empty search result displays a clear message");
        Check(fixture.Controller.Totals == beforeSearch && fixture.Controller.Lines[0].Quantity == 2,
            "Filtering products preserves the unpaid cart");
        search.Text = "ca phe";
        Check(products.Controls.OfType<Button>().Count() == 2, "Vietnamese search works without accents");
        search.Clear();
        category.SelectedItem = "Trà";
        Check(products.Controls.OfType<Button>().Count() == 3 && products.Controls.OfType<Button>().All(x => x.Tag is Product { Category: "Trà" }),
            "Category filtering displays only matching products");
        category.SelectedIndex = 0;
        Field<TextBox>(sales, "_promoCode").Text = "SALE10";
        Click(Field<Button>(sales, "_applyPromo"));
        Check(fixture.Controller.Promotion?.Code == "SALE10" && fixture.Controller.Totals.Payable == 45000m,
            "Applying a valid promotion updates the actual cart total");
        Field<TextBox>(sales, "_promoCode").Text = "KHONG-TON-TAI";
        Click(Field<Button>(sales, "_applyPromo"));
        Check(fixture.Controller.Promotion?.Code == "SALE10" && fixture.Controller.Totals.Payable == 45000m &&
            Field<Label>(sales, "_promoStatus").Text.Contains("Không tìm thấy"),
            "An invalid promotion keeps the valid discount and shows an inline error");
        Snapshot(sales, "sales-promotion-inline.png");

        var item = fixture.Controller.FindProduct("MACCHIATO");
        using var options = new OptionsForm(item);
        Prepare(options, new Size(610, 680));
        Descendants(options).OfType<RadioButton>().Single(x => x.Text.StartsWith("Lớn (L)")).Checked = true;
        Descendants(options).OfType<CheckBox>().Single(x => x.Text.StartsWith("Kem sữa")).Checked = true;
        Field<NumericUpDown>(options, "_quantity").Value = 2;
        Check(Field<Label>(options, "_price").Text.Contains(CartRules.Money(104000)),
            "Option controls recalculate size, topping and quantity price");
        var selected = options.Selections.ToArray();
        var quantity = options.Quantity;
        Snapshot(options, "options-selected.png");
        Click((Button)options.AcceptButton!);
        Check(options.DialogResult == DialogResult.OK && selected.Length == 2,
            "Saving valid option controls returns an accepted selection");
        var line = fixture.Controller.AddItemToCart(item.Id, quantity, selected);
        Invoke(sales, "RefreshCart", line.LineId);
        var grid = Field<DataGridView>(sales, "_cart");
        Check(grid.Rows.Count == 2 && grid.Rows.Cast<DataGridViewRow>().Any(x => x.Cells[0].Value?.ToString()?.Contains("Kem sữa") == true),
            "The cart displays selected options on the matching row");
        Snapshot(sales, "sales-1360x800.png");
        CheckInside(sales, Field<Button>(sales, "_checkout"), "Checkout stays within the normal window");
        sales.ClientSize = new Size(1100, 680);
        Snapshot(sales, "sales-1100x680.png");
        CheckInside(sales, Field<Button>(sales, "_checkout"), "Checkout stays within the smaller window");
        CheckInside(sales, Field<Button>(sales, "_applyPromo"), "Promotion actions stay within the smaller window");
        Check(Field<DataGridView>(sales, "_cart").Height >= 90,
            "The smaller window retains a usable cart list; height=" + Field<DataGridView>(sales, "_cart").Height);
        Check(fixture.Orders.Saved.Count == 0, "Search, option editing and promotions create no transactions");
    }

    private static void PaymentAndHistory()
    {
        var fixture = CreateCashier();
        Await(fixture.Controller.InitializeAsync());
        fixture.Controller.AddItemToCart("CA-PHE-DEN", 2, []);
        using (var cash = new PaymentForm(fixture.Controller))
        {
            Prepare(cash, new Size(650, 550));
            Field<NumericUpDown>(cash, "_cash").Value = 49000;
            Check(!Field<Button>(cash, "_confirm").Enabled && Field<Label>(cash, "_change").Text.Contains("còn thiếu"),
                "Insufficient cash disables confirmation and explains the shortage");
            Snapshot(cash, "payment-cash-short.png");
            Field<NumericUpDown>(cash, "_cash").Value = 100000;
            Check(Field<Button>(cash, "_confirm").Enabled && Field<Label>(cash, "_change").Text.Contains(CartRules.Money(50000)),
                "Enough cash enables confirmation and displays the correct change");
        }
        using (var transfer = new PaymentForm(fixture.Controller))
        {
            Prepare(transfer, new Size(650, 550));
            Field<ComboBox>(transfer, "_method").SelectedIndex = 1;
            Check(!Field<Button>(transfer, "_confirm").Enabled && !Field<NumericUpDown>(transfer, "_cash").Enabled,
                "Bank transfer requires an explicit receipt confirmation");
            Field<CheckBox>(transfer, "_transfer").Checked = true;
            Check(Field<Button>(transfer, "_confirm").Enabled, "Checking the received-transfer box enables payment");
            Snapshot(transfer, "payment-transfer-confirmed.png");
            Await(InvokeTask(transfer, "ExecuteAsync", false));
            Check(transfer.CompletedReceipt?.Payment.Method == PaymentMethod.BankTransfer && fixture.Orders.Saved.Count == 1,
                "The payment form records exactly one completed bank-transfer receipt");
        }
        var receipt = fixture.Controller.PaidReceipt ?? throw new Exception("Payment did not produce a receipt.");
        using (var printer = new ReceiptPrinter(receipt))
        using (var page = new Bitmap(320, 400))
        using (var graphics = Graphics.FromImage(page))
        {
            page.SetResolution(100, 100);
            var pages = 0;
            bool more;
            do
            {
                graphics.Clear(Color.White);
                var args = new System.Drawing.Printing.PrintPageEventArgs(graphics,
                    new Rectangle(20, 20, 280, 360), new Rectangle(0, 0, 320, 400),
                    new System.Drawing.Printing.PageSettings());
                typeof(ReceiptPrinter).GetMethod("OnPrintPage", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(printer, [null, args]);
                more = args.HasMorePages;
                page.Save(Path.Combine(_output, $"receipt-print-{++pages}.png"), ImageFormat.Png);
                if (pages > 10) throw new Exception("Receipt pagination did not finish.");
            } while (more);
            Check(pages > 1, "Receipt printing wraps narrow paper and completes across multiple pages");
        }
        using (var display = new ReceiptForm(receipt))
        {
            Prepare(display, new Size(760, 750));
            Check(Descendants(display).Any(x => x.AccessibleRole == AccessibleRole.Document && (x.AccessibleName?.Contains(receipt.ReceiptNumber) ?? false)),
                "Receipt viewer displays the saved receipt number");
            Snapshot(display, "receipt.png");
        }
        using (var history = new ReceiptHistoryForm(fixture.Orders, true))
        {
            Prepare(history, new Size(1120, 700));
            Until(() => !Field<bool>(history, "_loading") && Field<DataGridView>(history, "_list").Rows.Count == 1);
            Check(Field<DataGridView>(history, "_list").Rows.Count == 1 && Field<Button>(history, "_create").Enabled,
                "History loads and selects a persisted receipt for a similar order");
            Snapshot(history, "history.png");
            Field<TextBox>(history, "_search").Text = "khong-tim-thay-9876";
            Check(Field<DataGridView>(history, "_list").Rows.Count == 0 && !Field<Button>(history, "_create").Enabled,
                "History disables creation when filtering finds no receipt");
            Field<TextBox>(history, "_search").Clear();
            Click(Field<Button>(history, "_create"));
            Check(history.SelectedReceipt?.OrderId == receipt.OrderId && history.DialogResult == DialogResult.OK,
                "Create-similar selects the existing receipt without saving another transaction");
        }
        using (var readOnly = new ReceiptHistoryForm(fixture.Orders, false))
        {
            Prepare(readOnly, new Size(1120, 700));
            Until(() => !Field<bool>(readOnly, "_loading") && Field<DataGridView>(readOnly, "_list").Rows.Count == 1);
            Check(!Field<Button>(readOnly, "_create").Enabled && Field<Button>(readOnly, "_view").Enabled,
                "A restricted history session permits viewing while disabling similar orders");
        }
        Check(fixture.Orders.Saved.Count == 1, "Viewing and searching history leaves the transaction count unchanged");
    }

    private static void ShiftReconciliation()
    {
        var catalog = new MemoryCatalog();
        var shifts = new MemoryShifts();
        var transactions = new MemoryTransactions
        {
            Receipts = [MakeReceipt("UI-CA", PaymentMethod.Cash, 250000), MakeReceipt("UI-CA", PaymentMethod.BankTransfer, 150000)]
        };
        var controller = new ManagerController(catalog, new MemoryAccounts(), shifts, transactions, () => Now);
        Await(controller.LoginAsync("smoke", "smoke"));
        Await(controller.OpenShiftAsync("UI-CA", 500000));
        using var host = new Form { Text = "Ca làm việc & đối soát", AutoScaleMode = AutoScaleMode.Dpi, AutoScaleDimensions = new SizeF(96, 96) };
        using var panel = new ShiftManagementPanel(controller);
        host.Controls.Add(panel);
        Prepare(host, new Size(1100, 700));
        Await(panel.RefreshDataAsync());
        var actual = Named<NumericUpDown>(panel, "ActualCash");
        Check(actual.Value == 0, "A new reconciliation requires the actual counted amount instead of prefilled expected cash");
        Check(Named<Label>(panel, "ExpectedCash").Text == ManagerMoney(750000),
            "Expected cash includes opening cash plus cash payments and excludes bank transfers");
        Check(Named<Label>(panel, "CashDifference").Text.Contains("750") && Named<Label>(panel, "CashDifference").Text.Contains("Thiếu"),
            "A real zero count displays the full shortage");
        actual.Value = 740000;
        Await(panel.RefreshDataAsync());
        Check(actual.Value == 740000 && Named<Label>(panel, "CashDifference").Text.Contains("10"),
            "Refreshing preserves the counted amount and updates its shortage");
        actual.Value = 0;
        Await(panel.RefreshDataAsync());
        Check(actual.Value == 0, "Refreshing also preserves an explicitly counted zero");
        actual.Value = 750000;
        Check(Named<Label>(panel, "CashDifference").Text.StartsWith("0 đ"), "Matching counted cash clears the shortage");
        Snapshot(host, "shift-1100x700.png");
        var actions = Named<FlowLayoutPanel>(panel, "ShiftReconciliationActions");
        Check(actions.Controls.OfType<Button>().All(x => x.Top >= 0 && x.Bottom <= actions.ClientSize.Height),
            "Wrapped reconciliation buttons fit inside their auto-sized action row");
        Check(actions.Controls.OfType<Button>().All(x => x.Left >= 0 && x.Right <= actions.ClientSize.Width),
            "All reconciliation buttons fit horizontally at the smaller size");
        var selectedShift = "";
        panel.OnViewShiftTransactionsRequested += id => selectedShift = id;
        Click(Named<Button>(panel, "ViewShiftTransactions"));
        Check(selectedShift == "UI-CA", "View-transactions requests the current shift");
        Check(Named<Label>(panel, "ReconciliationFormula").Text.Contains("không gồm chuyển khoản") &&
            !Descendants(panel).OfType<Label>().Any(x => x.Text.Contains("lý thuyết")),
            "Concise labels and the reconciliation formula explain the amounts");
        host.ClientSize = new Size(1450, 900);
        Snapshot(host, "shift-1450x900.png");
        Check(shifts.Shifts.Single().Status == ShiftStatus.Open && transactions.Receipts.Length == 2,
            "Reconciliation previews leave the shift and transactions unchanged");
    }

    private static (CashierController Controller, MemoryOrders Orders) CreateCashier()
    {
        var source = new JsonSalesDataSource(Path.Combine(AppContext.BaseDirectory, "Data", "DemoCatalog.json"), new DemoSessionProvider());
        var orders = new MemoryOrders();
        return (new CashierController(source, orders, new MemoryJournal(), () => Now), orders);
    }

    private static void CatalogRefreshAfterCheckout()
    {
        var read = BusinessJson.ReadAsync<CatalogData>(Path.Combine(AppContext.BaseDirectory, "Data", "DemoCatalog.json"));
        Await(read);
        var catalog = read.Result!;
        var source = new MutableSource(new SalesData(catalog.Menu, catalog.Promotions,
            new CashierSession("UI-01", "Nguyễn Tuấn Khôi", "UI-CA", true, true, true)));
        var orders = new MemoryOrders();
        var controller = new CashierController(source, orders, new MemoryJournal(), () => Now);
        using var sales = new SalesForm(controller, Path.Combine(_output, "test-data"), orders);
        Prepare(sales, new Size(1360, 800));
        Until(() => Field<bool>(sales, "_loaded") && Field<FlowLayoutPanel>(sales, "_products").Controls.Count > 0);
        var products = Field<FlowLayoutPanel>(sales, "_products");
        var category = Field<ComboBox>(sales, "_category");
        category.SelectedItem = "Cà phê";
        var original = products.Controls.OfType<Button>().Single(x => x.Tag is Product { Id: "CA-PHE-DEN" });
        Click(original);
        source.Data = source.Data with
        {
            Menu = source.Data.Menu.Select(x => x.Id == "CA-PHE-DEN" ? x with { BasePrice = 27000 } :
                x.Id == "CA-PHE-SUA" ? x with { OptionGroups = [] } :
                x.Id == "GA" ? x with { Available = true } : x).ToImmutableArray()
        };
        var checkout = controller.Checkout(new PaymentRequest(PaymentMethod.Cash, 30000));
        Await(checkout);
        Check(checkout.Result.Kind == CheckoutResultKind.Rejected && orders.Saved.Count == 0,
            "Checkout rejects an old price after Manager changes the catalog");
        Invoke(sales, "RefreshCart", (object?)null);
        var refreshed = products.Controls.OfType<Button>().Single(x => x.Tag is Product { Id: "CA-PHE-DEN" });
        Check(refreshed.Tag is Product { BasePrice: 27000 } && !ReferenceEquals(original, refreshed) && original.IsDisposed,
            "Cart refresh replaces stale product metadata after a changed catalog snapshot");
        Check(category.SelectedItem?.ToString() == "Cà phê" && products.Controls.OfType<Button>().Any(x =>
            x.Tag is Product { Id: "CA-PHE-SUA", OptionGroups.Length: 0 }),
            "Refreshed option metadata preserves the selected category");
        category.SelectedIndex = 0;
        Check(products.Controls.OfType<Button>().Single(x => x.Tag is Product { Id: "GA" }).Enabled,
            "Availability changes from Manager update the product cards");
    }

    private static void EditUnavailableOptions()
    {
        var item = new Product("OPTIONS", "Món kiểm thử", "Cà phê", 40000, true,
            [new("SIZE", "Kích cỡ", 1, 1, [new("M", "Vừa (M)", 0), new("L", "Lớn (L)", 5000)]),
             new("TOP", "Topping", 0, 2, [new("PEARL", "Trân châu", 5000), new("CREAM", "Kem sữa", 7000)])]);
        var previous = new CartLine(Guid.NewGuid(), item.Id, item.Name, item.BasePrice,
            CartRules.ApplyOption(item, [new("SIZE", "L"), new("TOP", "PEARL")]), 1);
        var noPearl = item with { OptionGroups = item.OptionGroups.Select(group => group with
        {
            Choices = group.Choices.Select(choice => choice.Id == "PEARL" ? choice with { Available = false } : choice).ToImmutableArray()
        }).ToImmutableArray() };
        using (var form = new OptionsForm(noPearl, previous))
        {
            Prepare(form, new Size(610, 680));
            var pearl = Descendants(form).OfType<CheckBox>().Single(x => x.Text.StartsWith("Trân châu"));
            Check(!pearl.Enabled && !pearl.Checked && pearl.Text.Contains("Tạm ngừng"),
                "An unavailable old topping is visibly disabled and excluded from the edited selection");
            Check(Field<Label>(form, "_error").Text.Contains("tùy chọn cũ") && Field<Label>(form, "_price").Text.Contains(CartRules.Money(45000)),
                "The edit dialog explains the unavailable option and recalculates the payable line price");
            Click((Button)form.AcceptButton!);
            Check(form.DialogResult == DialogResult.OK && !form.Selections.Any(x => x.ChoiceId == "PEARL"),
                "Cashier can save the corrected line without deleting and recreating the whole item");
        }
        var noLarge = item with { OptionGroups = item.OptionGroups.Select(group => group with
        {
            Choices = group.Choices.Select(choice => choice.Id == "L" ? choice with { Available = false } : choice).ToImmutableArray()
        }).ToImmutableArray() };
        using var required = new OptionsForm(noLarge, previous);
        Prepare(required, new Size(610, 680));
        Check(!required.Selections.Any(x => x.GroupId == "SIZE"), "An unavailable required size is not silently replaced with a different size");
        Click((Button)required.AcceptButton!);
        Check(required.DialogResult != DialogResult.OK && Field<Label>(required, "_error").Text.Length > 0,
            "The cashier must select an available required size before saving");
        Descendants(required).OfType<RadioButton>().Single(x => x.Text.StartsWith("Vừa (M)")).Checked = true;
        Click((Button)required.AcceptButton!);
        Check(required.DialogResult == DialogResult.OK && required.Selections.Any(x => x.ChoiceId == "M"),
            "Choosing an available size permits saving the corrected selection");
    }

    private static void SessionRecovery()
    {
        var read = BusinessJson.ReadAsync<CatalogData>(Path.Combine(AppContext.BaseDirectory, "Data", "DemoCatalog.json"));
        Await(read);
        var catalog = read.Result!;
        var source = new MutableSource(new SalesData(catalog.Menu, catalog.Promotions,
            new CashierSession("UI-01", "Thu ngân kiểm thử", "CA-DONG", false, false, true)));
        var controller = new CashierController(source, new MemoryOrders(), new MemoryJournal(), () => Now);
        using var form = new SalesForm(controller, Path.Combine(_output, "session-recovery"));
        Prepare(form, new Size(1100, 680));
        Until(() => Field<bool>(form, "_loaded"));
        var refresh = Field<Button>(form, "_refresh");
        Check(refresh.Enabled && !Field<Button>(form, "_checkout").Enabled,
            "A closed/revoked cashier session permits reload while blocking checkout");
        source.Data = source.Data with { Session = source.Data.Session with { ShiftId = "CA-MO", CanSell = true, ShiftOpen = true } };
        Click(refresh);
        Until(() => controller.CanEdit && !Field<bool>(form, "_busy"));
        Check(Field<Button>(form, "_checkout").Enabled && Field<Label>(form, "_session").Text.Contains("CA-MO"),
            "Reload reads the restored session and enables the cashier controls");
        Check(Field<FlowLayoutPanel>(form, "_products").Controls.OfType<Button>().Any(x => x.Enabled),
            "Product cards become selectable after session recovery");
    }

    private static void CloseDuringLoad()
    {
        var source = new DelayedSource();
        var controller = new CashierController(source, new MemoryOrders(), new MemoryJournal(), () => Now);
        using var form = new SalesForm(controller, Path.Combine(_output, "delayed-loading"));
        var loading = InvokeTask(form, "LoadAsync");
        Check(source.ReadRequested && !loading.IsCompleted, "The test closes Cashier while a real asynchronous load is pending");
        var products = Field<FlowLayoutPanel>(form, "_products");
        form.Dispose();
        source.Complete(new SalesData([], [], new CashierSession("UI", "Thu ngân", "CA", true, true, true)));
        Await(loading);
        Check(form.IsDisposed && !Field<bool>(form, "_loaded") && products.Controls.Count == 0,
            "Closing during loading creates no controls or error dialog after disposal");
    }

    private static void SharedJournalLease()
    {
        var path = Path.Combine(_output, "journal-lease-" + Guid.NewGuid().ToString("N"));
        var fixture = CreateCashier();
        using (var first = new SalesForm(fixture.Controller, path))
        {
            var rejected = false;
            try { using var duplicate = new SalesForm(CreateCashier().Controller, path); }
            catch (BusinessException) { rejected = true; }
            Check(rejected, "Two Cashier windows in one host cannot share a pending-checkout journal");
            Check(ProbeLease(path) == 3, "A second process cannot open Cashier against the same journal");
        }
        Check(ProbeLease(path) == 0, "Closing Cashier releases its journal lease for the next process");
        using var modalStyle = new SalesForm(CreateCashier().Controller, path);
        Prepare(modalStyle, new Size(1100, 680));
        Until(() => Field<bool>(modalStyle, "_loaded"));
        modalStyle.Close();
        Check(ProbeLease(path) == 0, "The FormClosed event releases the journal before the host disposes the dialog");
        using var reopened = new SalesForm(CreateCashier().Controller, path);
        Check(!reopened.IsDisposed, "An existing host can reopen Cashier after the previous dialog closes");
    }

    private static int ProbeLease(string path)
    {
        var executable = Environment.ProcessPath ?? throw new Exception("Missing executable path.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--lease-probe");
        start.ArgumentList.Add(path);
        using var child = Process.Start(start) ?? throw new Exception("Cannot start lease probe.");
        if (!child.WaitForExit(10000)) { child.Kill(); throw new TimeoutException("Lease probe timed out."); }
        return child.ExitCode;
    }

    private static void Prepare(Form form, Size clientSize)
    {
        form.ShowInTaskbar = false;
        form.Opacity = 0;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-30000, -30000);
        _ = form.Handle;
        foreach (var child in Descendants(form)) _ = child.Handle;
        form.PerformAutoScale();
        form.ClientSize = clientSize;
        // Native grids and controls added after async loading need effective
        // visibility to lay out and paint correctly. Keep the top-level window
        // completely transparent, offscreen, absent from the taskbar and unable
        // to activate; this produces no visible dialog or focus change.
        const int extendedStyle = -20;
        const int noActivate = 0x08000000;
        const int toolWindow = 0x00000080;
        SetWindowLong(form.Handle, extendedStyle, GetWindowLong(form.Handle, extendedStyle) | noActivate | toolWindow);
        form.Show();
        form.ClientSize = clientSize;
        Layout(form);
    }

    private static void Snapshot(Form form, string name)
    {
        Layout(form);
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(_output, name), ImageFormat.Png);
        Log($"PNG | {name} | client={form.ClientSize.Width}x{form.ClientSize.Height} | DPI={form.DeviceDpi}");
    }

    private static void Layout(Control control)
    {
        for (var i = 0; i < 4; i++)
        {
            control.PerformLayout();
            foreach (var child in Descendants(control)) child.PerformLayout();
            Application.DoEvents();
        }
    }

    private static void CheckInside(Form form, Control control, string description)
    {
        Layout(form);
        var bounds = form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
        Check(bounds.Width > 0 && bounds.Height > 0 && form.ClientRectangle.Contains(bounds), description + $" [{bounds}]");
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static T Named<T>(Control root, string name) where T : Control =>
        Descendants(root).OfType<T>().Single(x => x.Name == name);
    private static T Field<T>(object target, string name) =>
        (T)(target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target)
            ?? throw new Exception("Missing field: " + name));
    private static object? Invoke(object target, string name, params object?[] args) =>
        (target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new Exception("Missing method: " + name)).Invoke(target, args);
    private static Task InvokeTask(object target, string name, params object?[] args) => (Task)Invoke(target, name, args)!;

    private static void Click(Button button)
    {
        if (!button.Enabled) throw new Exception("Cannot click disabled control: " + button.Text);
        // Invoke the native Button's wired Click handler while keeping the form hidden.
        typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(button, [EventArgs.Empty]);
        Application.DoEvents();
    }

    private static void Await(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("UI operation exceeded 30 seconds.");
            Application.DoEvents();
            Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
    }

    private static void Until(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("UI loading exceeded 30 seconds.");
            Application.DoEvents();
            Thread.Sleep(5);
        }
        Application.DoEvents();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);

    private static void Run(string name, Action body)
    {
        try { body(); }
        catch (Exception exception)
        {
            _failed++;
            Log("FAIL | " + name + " | " + (exception.InnerException ?? exception));
        }
    }
    private static void Check(bool condition, string description)
    {
        if (condition) _passed++; else _failed++;
        Log((condition ? "PASS | " : "FAIL | ") + description);
    }
    private static void Log(string text)
    {
        Results.Add(text);
        Console.WriteLine(text);
        if (_output.Length > 0) File.WriteAllLines(Path.Combine(_output, "UiSmokeResults.txt"), Results, new UTF8Encoding(true));
    }
    private static string ManagerMoney(decimal amount) => $"{amount:N0} đ";

    private static Receipt MakeReceipt(string shift, PaymentMethod method, decimal amount) => new(
        Guid.NewGuid(), "HD-UI-" + Guid.NewGuid().ToString("N")[..8], Now, "UI-01", "Thu ngân kiểm thử", shift,
        [new CartLine(Guid.NewGuid(), "CA-PHE-DEN", "Cà phê đen", amount, [], 1)], null,
        new CartTotals(amount, 0, amount), new PaymentSnapshot(method, amount, 0), false);

    private sealed class MemoryOrders : IOrderRepository, IReceiptHistory
    {
        public List<Receipt> Saved { get; } = [];
        public Task<Receipt> SaveAsync(Receipt receipt, CancellationToken cancellationToken = default)
        {
            var existing = Saved.FirstOrDefault(x => x.OrderId == receipt.OrderId);
            if (existing is not null) return Task.FromResult(existing);
            Saved.Add(receipt);
            return Task.FromResult(receipt);
        }
        public Task<Receipt?> FindAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved.FirstOrDefault(x => x.OrderId == orderId));
        public Task<ReceiptHistoryResult> ReadRecentAsync(int limit = 100, CancellationToken token = default) =>
            Task.FromResult(new ReceiptHistoryResult(Saved.OrderByDescending(x => x.PaidAt).Take(limit).ToImmutableArray(), 0));
    }
    private sealed class MutableSource(SalesData data) : ICashierDataSource
    {
        public SalesData Data { get; set; } = data;
        public Task<SalesData> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Data);
    }
    private sealed class DelayedSource : ICashierDataSource
    {
        private readonly TaskCompletionSource<SalesData> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ReadRequested { get; private set; }
        public Task<SalesData> ReadAsync(CancellationToken cancellationToken = default)
        {
            // Deliberately ignore cancellation, like an adapter whose read is already in flight.
            ReadRequested = true;
            return _completion.Task;
        }
        public void Complete(SalesData data) => _completion.TrySetResult(data);
    }
    private sealed class MemoryJournal : ICheckoutJournal
    {
        private Receipt? _pending;
        public Task<Receipt?> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_pending);
        public Task WriteAsync(Receipt candidate, CancellationToken cancellationToken = default) { _pending = candidate; return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken cancellationToken = default) { _pending = null; return Task.CompletedTask; }
    }
    private sealed class MemoryCatalog : ICatalogStore
    {
        public Task<ImmutableArray<Product>> GetMenuAsync(CancellationToken token = default) => Task.FromResult(ImmutableArray<Product>.Empty);
        public Task SaveMenuAsync(ImmutableArray<Product> menu, CancellationToken token = default) => Task.CompletedTask;
        public Task<ImmutableArray<Promotion>> GetPromotionsAsync(CancellationToken token = default) => Task.FromResult(ImmutableArray<Promotion>.Empty);
        public Task SavePromotionsAsync(ImmutableArray<Promotion> promotions, CancellationToken token = default) => Task.CompletedTask;
    }
    private sealed class MemoryAccounts : IAccountStore
    {
        public Task<ImmutableArray<StaffAccount>> GetAccountsAsync(CancellationToken token = default) => Task.FromResult<ImmutableArray<StaffAccount>>(
            [new("UI-01", "Nguyễn Tuấn Khôi", "smoke", "smoke", UserRole.Manager)]);
        public Task SaveAccountsAsync(ImmutableArray<StaffAccount> accounts, CancellationToken token = default) => Task.CompletedTask;
    }
    private sealed class MemoryShifts : IShiftStore
    {
        public List<ShiftRecord> Shifts { get; } = [];
        public Task<ImmutableArray<ShiftRecord>> GetShiftsAsync(CancellationToken token = default) => Task.FromResult(Shifts.ToImmutableArray());
        public Task<ShiftRecord?> GetCurrentOpenShiftAsync(CancellationToken token = default) => Task.FromResult(Shifts.FirstOrDefault(x => x.Status == ShiftStatus.Open));
        public Task SaveShiftAsync(ShiftRecord shift, CancellationToken token = default)
        {
            var index = Shifts.FindIndex(x => x.ShiftId == shift.ShiftId);
            if (index < 0) Shifts.Add(shift); else Shifts[index] = shift;
            return Task.CompletedTask;
        }
    }
    private sealed class MemoryTransactions : ITransactionReader
    {
        public ImmutableArray<Receipt> Receipts { get; init; } = [];
        public Task<ImmutableArray<Receipt>> ReadAllReceiptsAsync(CancellationToken token = default) => Task.FromResult(Receipts);
    }
}
