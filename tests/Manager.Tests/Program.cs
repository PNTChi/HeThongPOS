using System.Collections.Immutable;
using Cashier.Core;
using Cashier.Infrastructure;
using Manager.Core;
using Manager.Infrastructure;

namespace Manager.Tests;

internal static class Program
{
    private static readonly DateTimeOffset BaseTime = DateTimeOffset.Parse("2026-10-08T10:00:00+07:00");
    private static readonly List<(string Name, Func<Task> Run)> TestCases = [];

    private static async Task<int> Main()
    {
        Add("TC_M01 Đăng nhập: tài khoản hợp lệ, sai mật khẩu, tài khoản bị khóa", async () =>
        {
            var f = CreateFixture();
            var admin = await f.Controller.LoginAsync("admin", "123456");
            Assert(admin.Role == UserRole.Manager, "Tài khoản admin phải có vai trò Quản lý.");
            Assert(admin.StaffId == "QL01", "Mã nhân viên khớp QL01.");

            await ThrowsAsync(() => f.Controller.LoginAsync("admin", "wrong_pass"));
            await ThrowsAsync(() => f.Controller.LoginAsync("non_existent", "123456"));

            // Inactive account
            var inactive = new StaffAccount("LOCK", "Bị Khóa", "lockuser", "123", UserRole.Cashier, IsActive: false);
            await f.AccountStore.SaveAccountsAsync([inactive, .. await f.AccountStore.GetAccountsAsync()]);
            await ThrowsAsync(() => f.Controller.LoginAsync("lockuser", "123"));
        });

        Add("TC_M02 Mở ca: bắt đầu ca thành công, chặn mở 2 ca cùng lúc, kiểm tra quyền", async () =>
        {
            var f = CreateFixture();
            await f.Controller.LoginAsync("admin", "123456");

            var shift = await f.Controller.OpenShiftAsync("CA-SANG-01", 1000000m);
            Assert(shift.Status == ShiftStatus.Open, "Ca mới phải ở trạng thái Open.");
            Assert(shift.StartingCash == 1000000m, "Số tiền đầu ca khớp.");

            // Cannot open second shift while first is open
            await ThrowsAsync(() => f.Controller.OpenShiftAsync("CA-SANG-02", 500000m));

            // Negative starting cash
            f.Controller.Logout();
            var noShiftUser = new StaffAccount("TN_NO_PERM", "Nhân viên", "noperm", "123", UserRole.Cashier, CanManageShift: false);
            await f.AccountStore.SaveAccountsAsync([noShiftUser, .. await f.AccountStore.GetAccountsAsync()]);
            await f.Controller.LoginAsync("noperm", "123");
            await ThrowsAsync(() => f.Controller.OpenShiftAsync("CA-NEW", 100000m));
        });

        Add("TC_M03 Đóng ca & đối soát: tính đúng tiền thực tế, tiền lý thuyết và chênh lệch", async () =>
        {
            var f = CreateFixture();
            await f.Controller.LoginAsync("admin", "123456");
            await f.Controller.OpenShiftAsync("CA-TEST", 500000m);

            // Add sample receipts for this shift
            var r1 = CreateReceipt("CA-TEST", PaymentMethod.Cash, 100000m);
            var r2 = CreateReceipt("CA-TEST", PaymentMethod.BankTransfer, 200000m);
            var r3 = CreateReceipt("CA-TEST", PaymentMethod.Cash, 150000m);
            f.TransactionReader.Receipts = [r1, r2, r3];

            // Preview reconciliation
            var recon = await f.Controller.PreviewShiftReconciliationAsync(740000m, "Kiểm quỹ cuối ca");
            Assert(recon.TotalOrders == 3, "Tổng 3 đơn hàng.");
            Assert(recon.TotalRevenue == 450000m, "Tổng doanh thu 450.000 đ.");
            Assert(recon.CashTotal == 250000m, "Tiền mặt bán được 250.000 đ.");
            Assert(recon.TransferTotal == 200000m, "Chuyển khoản 200.000 đ.");
            Assert(recon.ExpectedCash == 750000m, "Tiền mặt lý thuyết = 500.000 đầu ca + 250.000 bán = 750.000 đ.");
            Assert(recon.Difference == -10000m, "Chênh lệch thiếu 10.000 đ.");

            // Close shift
            var closed = await f.Controller.CloseShiftAsync(750000m, "Khớp tiền");
            Assert(closed.Status == ShiftStatus.Closed, "Ca đã được chuyển sang Closed.");
            Assert(closed.Difference == 0m, "Đóng ca khớp tiền chênh lệch = 0.");
            Assert(await f.Controller.GetCurrentOpenShiftAsync() == null, "Không còn ca nào đang mở.");
        });

        Add("TC_M04 Quản lý món: thêm món, sửa giá, kiểm tra giá âm và trùng mã", async () =>
        {
            var f = CreateFixture();
            await f.Controller.LoginAsync("admin", "123456");

            var newItem = new MenuItem("TRA-SOT", "Trà đào cam sả", "Trà", 35000m, true, []);
            await f.Controller.SaveMenuItemAsync(newItem, isNew: true);

            var menu = await f.Controller.GetMenuAsync();
            Assert(menu.Any(x => x.Id == "TRA-SOT"), "Món mới đã được lưu vào thực đơn.");

            // Reject duplicate ID
            await ThrowsAsync(() => f.Controller.SaveMenuItemAsync(newItem, isNew: true));

            // Reject negative price
            var invalidPrice = newItem with { BasePrice = -5000m };
            await ThrowsAsync(() => f.Controller.SaveMenuItemAsync(invalidPrice, isNew: false));

            // Update item
            var updated = newItem with { BasePrice = 38000m };
            await f.Controller.SaveMenuItemAsync(updated, isNew: false);
            menu = await f.Controller.GetMenuAsync();
            Assert(menu.First(x => x.Id == "TRA-SOT").BasePrice == 38000m, "Giá món đã cập nhật thành 38.000 đ.");

            // Delete item
            await f.Controller.DeleteMenuItemAsync("TRA-SOT");
            menu = await f.Controller.GetMenuAsync();
            Assert(!menu.Any(x => x.Id == "TRA-SOT"), "Món đã được xóa.");
        });

        Add("TC_M05 Quản lý Modifier: nhóm tùy chọn, giới hạn min/max, phụ thu không âm", async () =>
        {
            var f = CreateFixture();
            await f.Controller.LoginAsync("admin", "123456");

            var group = new OptionGroup("SIZE", "Kích cỡ", 1, 1,
            [
                new("M", "Size M", 0m, true),
                new("L", "Size L", 5000m, true)
            ]);

            var itemWithOptions = new MenuItem("CF-OP", "Cà phê sữa đá", "Cà phê", 25000m, true, [group]);
            await f.Controller.SaveMenuItemAsync(itemWithOptions, isNew: true);

            var saved = (await f.Controller.GetMenuAsync()).First(x => x.Id == "CF-OP");
            Assert(saved.OptionGroups.Length == 1, "Món có 1 nhóm tùy chọn.");
            Assert(saved.OptionGroups[0].Choices.Length == 2, "Nhóm có 2 lựa chọn.");

            // Invalid min > max
            var invalidGroup = group with { Minimum = 2, Maximum = 1 };
            await ThrowsAsync(() => f.Controller.SaveMenuItemAsync(itemWithOptions with { OptionGroups = [invalidGroup] }, isNew: false));

            // Negative extra price
            var invalidChoice = group with { Choices = [new("M", "Size M", -1000m, true)] };
            await ThrowsAsync(() => f.Controller.SaveMenuItemAsync(itemWithOptions with { OptionGroups = [invalidChoice] }, isNew: false));
        });

        Add("TC_M06 Quản lý Khuyến mãi: thêm mã, kiểm tra phần trăm <= 100, ngày hiệu lực", async () =>
        {
            var f = CreateFixture();
            await f.Controller.LoginAsync("admin", "123456");

            var promo = new Promotion("KM20", "Giảm 20% đơn từ 50k", DiscountKind.Percentage, 20m, 50000m,
                BaseTime.AddDays(-1), BaseTime.AddDays(10), true);
            await f.Controller.SavePromotionAsync(promo, isNew: true);

            var promos = await f.Controller.GetPromotionsAsync();
            Assert(promos.Any(x => x.Code == "KM20"), "Khuyến mãi KM20 đã được lưu.");

            // Duplicate code
            await ThrowsAsync(() => f.Controller.SavePromotionAsync(promo, isNew: true));

            // Percentage > 100%
            var invalidPercent = promo with { Code = "KM150", Value = 150m };
            await ThrowsAsync(() => f.Controller.SavePromotionAsync(invalidPercent, isNew: true));

            // StartsAt > EndsAt
            var invalidDate = promo with { Code = "KMDATE", StartsAt = BaseTime.AddDays(5), EndsAt = BaseTime.AddDays(1) };
            await ThrowsAsync(() => f.Controller.SavePromotionAsync(invalidDate, isNew: true));
        });

        Add("TC_M07 Quản lý Nhân viên: thêm nhân viên, kiểm tra trùng mã NV và trùng username", async () =>
        {
            var f = CreateFixture();
            await f.Controller.LoginAsync("admin", "123456");

            var staff = new StaffAccount("NV99", "Nhân viên mới", "staff99", "pass123", UserRole.Cashier);
            await f.Controller.SaveAccountAsync(staff, isNew: true);

            var accounts = await f.Controller.GetAccountsAsync();
            Assert(accounts.Any(x => x.StaffId == "NV99"), "Nhân viên NV99 đã được thêm.");

            // Duplicate StaffId
            await ThrowsAsync(() => f.Controller.SaveAccountAsync(staff with { Username = "otheruser" }, isNew: true));

            // Duplicate Username
            await ThrowsAsync(() => f.Controller.SaveAccountAsync(staff with { StaffId = "NV100" }, isNew: true));
        });

        Add("TC_M08 Tra cứu lịch sử: lọc theo thời gian, theo ca, theo phương thức", async () =>
        {
            var f = CreateFixture();
            await f.Controller.LoginAsync("admin", "123456");

            var r1 = CreateReceipt("CA-01", PaymentMethod.Cash, 50000m, BaseTime.AddHours(-3));
            var r2 = CreateReceipt("CA-01", PaymentMethod.BankTransfer, 80000m, BaseTime.AddHours(-2));
            var r3 = CreateReceipt("CA-02", PaymentMethod.Cash, 120000m, BaseTime.AddHours(-1));
            f.TransactionReader.Receipts = [r1, r2, r3];

            // Filter by shift
            var shift1Results = await f.Controller.QueryTransactionsAsync(new HistoryFilter(ShiftQuery: "CA-01"));
            Assert(shift1Results.Length == 2, "Ca CA-01 có 2 đơn hàng.");

            // Filter by payment method
            var transferResults = await f.Controller.QueryTransactionsAsync(new HistoryFilter(Method: PaymentMethod.BankTransfer));
            Assert(transferResults.Length == 1, "Chỉ có 1 đơn chuyển khoản.");
            Assert(transferResults[0].Totals.Payable == 80000m, "Số tiền đơn chuyển khoản là 80.000 đ.");
        });

        Add("TC_M09 Báo cáo doanh thu & món bán chạy & phân bổ phương thức", async () =>
        {
            var f = CreateFixture();
            await f.Controller.LoginAsync("admin", "123456");

            var r1 = CreateReceipt("CA-01", PaymentMethod.Cash, 50000m, BaseTime.AddHours(-2));
            var r2 = CreateReceipt("CA-01", PaymentMethod.BankTransfer, 150000m, BaseTime.AddHours(-1));
            f.TransactionReader.Receipts = [r1, r2];

            var report = await f.Controller.GenerateReportAsync(ReportKind.PaymentBreakdown, BaseTime.AddDays(-1), BaseTime.AddDays(1));
            Assert(report.TotalOrders == 2, "Tổng 2 đơn.");
            Assert(report.TotalRevenue == 200000m, "Tổng doanh thu 200.000 đ.");
            Assert(report.CashTotal == 50000m, "Tiền mặt 50.000 đ.");
            Assert(report.TransferTotal == 150000m, "Chuyển khoản 150.000 đ.");

            // Top products
            var prodReport = await f.Controller.GenerateReportAsync(ReportKind.TopProducts, BaseTime.AddDays(-1), BaseTime.AddDays(1));
            Assert(prodReport.TopProducts.Length > 0, "Có thống kê sản phẩm bán chạy.");
        });

        Add("TC_M10 Cầu nối Session: cung cấp phiên hợp lệ và đồng bộ với Cashier", async () =>
        {
            var f = CreateFixture();
            await f.Controller.LoginAsync("admin", "123456");
            var bridge = new ManagerCashierSessionProvider(f.Controller);

            var sessionBeforeShift = await bridge.GetCurrentAsync();
            Assert(sessionBeforeShift.StaffId == "QL01", "Mã nhân viên QL01.");
            Assert(!sessionBeforeShift.ShiftOpen, "Chưa mở ca thì ShiftOpen phải là false.");

            await f.Controller.OpenShiftAsync("CA-ACTIVE-01", 1000000m);
            var sessionAfterShift = await bridge.GetCurrentAsync();
            Assert(sessionAfterShift.ShiftOpen, "Sau khi mở ca thì ShiftOpen là true.");
            Assert(sessionAfterShift.ShiftId == "CA-ACTIVE-01", "Mã ca khớp.");
            Assert(!sessionAfterShift.IsDemo, "IsDemo là false cho phiên thực tế.");
        });

        Add("TC_M11 Lưu trữ JSON: kiểm tra ghi và đọc file thật", async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "mgr-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var catalogPath = Path.Combine(tempDir, "catalog.json");
                var catStore = new JsonCatalogStore(catalogPath);
                var testMenu = ImmutableArray.Create(new MenuItem("TEST1", "Cà phê test", "Test", 20000m, true, []));
                await catStore.SaveMenuAsync(testMenu);

                var loaded = await catStore.GetMenuAsync();
                Assert(loaded.Length == 1 && loaded[0].Id == "TEST1", "Menu đọc lại từ file JSON chính xác.");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        });

        Console.WriteLine("Manager automated business & store tests — " + DateTimeOffset.UtcNow.ToString("O"));
        var failed = 0;
        foreach (var (name, run) in TestCases)
        {
            try
            {
                await run();
                Console.WriteLine("PASS | " + name);
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine("FAIL | " + name + " | " + ex.Message);
            }
        }

        Console.WriteLine($"RESULT: {TestCases.Count - failed}/{TestCases.Count} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static void Add(string name, Func<Task> run) => TestCases.Add((name, run));
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task ThrowsAsync(Func<Task> action)
    {
        try { await action(); } catch (BusinessException) { return; }
        throw new Exception("Expected BusinessException.");
    }

    private static ManagerFixture CreateFixture()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mgr-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var catalogStore = new MemoryCatalogStore();
        var accountStore = new MemoryAccountStore();
        var shiftStore = new MemoryShiftStore();
        var transactionReader = new MemoryTransactionReader();

        var controller = new ManagerController(catalogStore, accountStore, shiftStore, transactionReader, () => BaseTime);
        return new ManagerFixture(controller, catalogStore, accountStore, shiftStore, transactionReader, tempDir);
    }

    private static Receipt CreateReceipt(string shiftId, PaymentMethod method, decimal payable, DateTimeOffset? paidAt = null)
    {
        var line = new CartLine(Guid.NewGuid(), "CA-PHE", "Cà phê đen", payable, [], 1);
        return new Receipt(
            OrderId: Guid.NewGuid(),
            ReceiptNumber: "HD-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            PaidAt: paidAt ?? BaseTime,
            StaffId: "QL01",
            StaffName: "Nguyễn Ngọc Yến Nhi",
            ShiftId: shiftId,
            Lines: [line],
            Promotion: null,
            Totals: new CartTotals(payable, 0, payable),
            Payment: new PaymentSnapshot(method, payable, 0),
            IsDemo: false);
    }

    private sealed record ManagerFixture(
        ManagerController Controller,
        MemoryCatalogStore CatalogStore,
        MemoryAccountStore AccountStore,
        MemoryShiftStore ShiftStore,
        MemoryTransactionReader TransactionReader,
        string TempDir);

    private sealed class MemoryCatalogStore : ICatalogStore
    {
        public ImmutableArray<MenuItem> Menu { get; set; } = [new("CA-PHE", "Cà phê đen", "Cà phê", 25000m, true, [])];
        public ImmutableArray<Promotion> Promotions { get; set; } = [];

        public Task<ImmutableArray<MenuItem>> GetMenuAsync(CancellationToken token = default) => Task.FromResult(Menu);
        public Task SaveMenuAsync(ImmutableArray<MenuItem> menu, CancellationToken token = default) { Menu = menu; return Task.CompletedTask; }
        public Task<ImmutableArray<Promotion>> GetPromotionsAsync(CancellationToken token = default) => Task.FromResult(Promotions);
        public Task SavePromotionsAsync(ImmutableArray<Promotion> promotions, CancellationToken token = default) { Promotions = promotions; return Task.CompletedTask; }
    }

    private sealed class MemoryAccountStore : IAccountStore
    {
        public ImmutableArray<StaffAccount> Accounts { get; set; } =
        [
            new("QL01", "Nguyễn Ngọc Yến Nhi", "admin", "123456", UserRole.Manager, true, true, true),
            new("TN01", "Nguyễn Tuấn Khôi", "khoi", "123456", UserRole.Cashier, true, true, true)
        ];

        public Task<ImmutableArray<StaffAccount>> GetAccountsAsync(CancellationToken token = default) => Task.FromResult(Accounts);
        public Task SaveAccountsAsync(ImmutableArray<StaffAccount> accounts, CancellationToken token = default) { Accounts = accounts; return Task.CompletedTask; }
    }

    private sealed class MemoryShiftStore : IShiftStore
    {
        public List<ShiftRecord> Shifts { get; } = [];
        public Task<ImmutableArray<ShiftRecord>> GetShiftsAsync(CancellationToken token = default) => Task.FromResult(Shifts.ToImmutableArray());
        public Task<ShiftRecord?> GetCurrentOpenShiftAsync(CancellationToken token = default) => Task.FromResult(Shifts.FirstOrDefault(s => s.Status == ShiftStatus.Open));
        public Task SaveShiftAsync(ShiftRecord shift, CancellationToken token = default)
        {
            var idx = Shifts.FindIndex(s => s.ShiftId.Equals(shift.ShiftId, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) Shifts[idx] = shift;
            else Shifts.Add(shift);
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryTransactionReader : ITransactionReader
    {
        public ImmutableArray<Receipt> Receipts { get; set; } = [];
        public Task<ImmutableArray<Receipt>> ReadAllReceiptsAsync(CancellationToken token = default) => Task.FromResult(Receipts);
    }
}
