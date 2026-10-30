using System.Collections.Immutable;
using Cashier.Core;
using Cashier.Infrastructure;

namespace Cashier.Tests;

internal static class Program
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-02T12:00:00+07:00");
    private static readonly List<(string Name, Func<Task> Run)> Cases = [];
    private static async Task<int> Main()
    {
        Add("TC01 Món thường, tổng tiền và tìm kiếm tiếng Việt", async () =>
        {
            var f = await Fixture.Create();
            f.Controller.AddItemToCart("plain", 2, []);
            Equal(50000m, f.Controller.Totals.Payable);
            Equal("ca phe sua", CartRules.SearchKey("Cà phê sữa"));
        });
        Add("TC02 Tùy chọn bắt buộc và phụ thu", async () =>
        {
            var f = await Fixture.Create();
            Throws(() => f.Controller.AddItemToCart("options", 1, []));
            var line = f.Controller.AddItemToCart("options", 2, [new("size", "L"), new("top", "pearl")]);
            Equal(50000m, line.UnitPrice);
            Equal(100000m, line.LineTotal);
        });
        Add("TC03 Chặn tùy chọn lạ, trùng và vượt số lượng", async () =>
        {
            var f = await Fixture.Create();
            Throws(() => f.Controller.AddItemToCart("options", 1, [new("size", "M"), new("size", "L")]));
            Throws(() => f.Controller.AddItemToCart("plain", 1, [new("top", "pearl")]));
            Throws(() => f.Controller.AddItemToCart("options", 1, [new("size", "M"), new("size", "M")]));
            Throws(() => f.Controller.AddItemToCart("options", 1, [new("size", "missing")]));
        });
        Add("TC04 Sửa số lượng, sửa tùy chọn, xóa dòng", async () =>
        {
            var f = await Fixture.Create();
            var line = f.Controller.AddItemToCart("options", 1, [new("size", "M")]);
            f.Controller.UpdateCartItem(line.LineId, 3, [new("size", "L")]);
            Equal(135000m, f.Controller.Totals.Payable);
            f.Controller.ChangeQuantity(line.LineId, 2);
            Equal(90000m, f.Controller.Totals.Payable);
            f.Controller.RemoveCartItem(line.LineId);
            Equal(0m, f.Controller.Totals.Payable);
            Equal(CheckoutResultKind.Rejected, (await f.Controller.Checkout(new(PaymentMethod.Cash, 0))).Kind);
        });
        Add("TC05 Phân biệt các tổ hợp tùy chọn", async () =>
        {
            var f = await Fixture.Create();
            var a = f.Controller.AddItemToCart("options", 1, [new("size", "M")]);
            var b = f.Controller.AddItemToCart("options", 1, [new("size", "L")]);
            Equal(2, f.Controller.Lines.Count);
            Assert(a.LineId != b.LineId, "Các dòng cần có định danh riêng.");
            Equal(85000m, f.Controller.Totals.Payable);
        });
        Add("TC06 Mã hợp lệ; mã sai/hết hạn giữ ưu đãi cũ", async () =>
        {
            var f = await Fixture.Create();
            f.Controller.AddItemToCart("plain", 2, []);
            f.Controller.ApplyPromotion(" sale10 ");
            Equal(45000m, f.Controller.Totals.Payable);
            Throws(() => f.Controller.ApplyPromotion("NOPE"));
            Throws(() => f.Controller.ApplyPromotion("EXPIRED"));
            Equal("SALE10", f.Controller.Promotion!.Code);
            Equal(45000m, f.Controller.Totals.Payable);
            f.Controller.RemovePromotion();
            Equal(50000m, f.Controller.Totals.Payable);
        });
        Add("TC07 Sửa giỏ bỏ mã và không giảm vượt tạm tính", async () =>
        {
            var f = await Fixture.Create();
            var line = f.Controller.AddItemToCart("plain", 2, []);
            f.Controller.ApplyPromotion("SALE10");
            f.Controller.ChangeQuantity(line.LineId, 3);
            Assert(f.Controller.Promotion is null, "Mã cần được kiểm tra lại sau sửa giỏ.");
            f.Controller.ApplyPromotion("BIG");
            Equal(0m, f.Controller.Totals.Payable);
            Equal(75000m, f.Controller.Totals.Discount);
        });
        Add("TC08 Tiền mặt 99.000 / nhận 100.000 / thừa 1.000", async () =>
        {
            var f = await Fixture.Create();
            f.Controller.AddItemToCart("ninety-nine", 1, []);
            Equal(CheckoutResultKind.Rejected, (await f.Controller.Checkout(new(PaymentMethod.Cash, 90000))).Kind);
            Equal(0, f.Orders.Values.Count);
            var paid = await f.Controller.Checkout(new(PaymentMethod.Cash, 100000));
            Equal(CheckoutResultKind.Paid, paid.Kind);
            Equal(1000m, paid.Receipt!.Payment.Change);
        });
        Add("TC09 Chuyển khoản cần xác nhận đã nhận tiền", async () =>
        {
            var f = await Fixture.Create();
            f.Controller.AddItemToCart("plain", 1, []);
            Equal(CheckoutResultKind.Rejected, (await f.Controller.Checkout(new(PaymentMethod.BankTransfer))).Kind);
            var paid = await f.Controller.Checkout(new(PaymentMethod.BankTransfer, TransferReceivedConfirmed: true));
            Equal(CheckoutResultKind.Paid, paid.Kind);
            Equal(0m, paid.Receipt!.Payment.Change);
        });
        Add("TC10 Xác nhận lặp/song song chỉ ghi một giao dịch", async () =>
        {
            var f = await Fixture.Create();
            f.Controller.AddItemToCart("plain", 1, []);
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.Controller.Checkout(new(PaymentMethod.Cash, 30000))));
            Assert(results.All(x => x.Kind == CheckoutResultKind.Paid), "Các xác nhận trả cùng hóa đơn.");
            Equal(1, f.Orders.SaveCalls);
            Equal(1, f.Orders.Values.Count);
            Equal(1, results.Select(x => x.Receipt!.OrderId).Distinct().Count());
        });
        Add("TC11 Lỗi sau commit khôi phục hóa đơn đã lưu", async () =>
        {
            var f = await Fixture.Create();
            f.Orders.FailAfterCommit = true;
            f.Controller.AddItemToCart("plain", 1, []);
            var result = await f.Controller.Checkout(new(PaymentMethod.Cash, 30000));
            Equal(CheckoutResultKind.Paid, result.Kind);
            Equal(1, f.Orders.Values.Count);
            Equal(CheckoutState.Paid, f.Controller.State);
        });
        Add("TC12 Không rõ kết quả khóa giỏ; kiểm tra trước thử lại", async () =>
        {
            var f = await Fixture.Create();
            f.Orders.FailBeforeCommit = true;
            f.Orders.FailReads = true;
            var line = f.Controller.AddItemToCart("plain", 1, []);
            var result = await f.Controller.Checkout(new(PaymentMethod.Cash, 30000));
            Equal(CheckoutResultKind.Uncertain, result.Kind);
            var id = f.Controller.PendingReceipt!.OrderId;
            Throws(() => f.Controller.ChangeQuantity(line.LineId, 2));
            Throws(() => f.Controller.StartNewOrder(true));
            f.Orders.FailReads = false;
            Equal(CheckoutResultKind.NotSaved, (await f.Controller.ResolvePendingAsync()).Kind);
            f.Orders.FailBeforeCommit = false;
            Equal(CheckoutResultKind.Paid, (await f.Controller.Checkout(new(PaymentMethod.Cash, 1))).Kind);
            Equal(id, f.Controller.PaidReceipt!.OrderId);
            Equal(30000m, f.Controller.PaidReceipt.Payment.AmountReceived);
        });
        Add("TC13 Khởi động lại tiếp tục cùng giao dịch đang chờ", async () =>
        {
            var f = await Fixture.Create();
            f.Orders.FailBeforeCommit = true;
            f.Controller.AddItemToCart("plain", 1, []);
            await f.Controller.Checkout(new(PaymentMethod.Cash, 30000));
            var pendingId = f.Controller.PendingReceipt!.OrderId;
            var restart = new CashierController(f.Source, f.Orders, f.Journal, () => Now);
            await restart.InitializeAsync();
            Equal(CheckoutState.Uncertain, restart.State);
            Equal(1, restart.Lines.Count);
            f.Orders.FailBeforeCommit = false;
            Equal(CheckoutResultKind.Paid, (await restart.Checkout(new(PaymentMethod.Cash, 0))).Kind);
            Equal(pendingId, restart.PaidReceipt!.OrderId);
        });
        Add("TC14 Phiên/ca không hợp lệ chặn thanh toán", async () =>
        {
            var f = await Fixture.Create();
            f.Source.Data = f.Source.Data with { Session = f.Source.Data.Session with { ShiftOpen = false } };
            f.Controller.AddItemToCart("plain", 1, []);
            Equal(CheckoutResultKind.Rejected, (await f.Controller.Checkout(new(PaymentMethod.Cash, 30000))).Kind);
            Equal(0, f.Orders.Values.Count);
        });
        Add("TC15 Món tạm ngừng, giá thay đổi chặn đơn cũ", async () =>
        {
            var f = await Fixture.Create();
            Throws(() => f.Controller.AddItemToCart("off", 1, []));
            f.Controller.AddItemToCart("plain", 1, []);
            f.Source.Data = f.Source.Data with { Menu = f.Source.Data.Menu.Select(x => x.Id == "plain" ? x with { BasePrice = 28000 } : x).ToImmutableArray() };
            Equal(CheckoutResultKind.Rejected, (await f.Controller.Checkout(new(PaymentMethod.Cash, 30000))).Kind);
            f.Controller.ChangeQuantity(f.Controller.Lines[0].LineId, 1);
            Equal(CheckoutResultKind.Paid, (await f.Controller.Checkout(new(PaymentMethod.Cash, 30000))).Kind);
        });
        Add("TC16 Mã hết hiệu lực trước checkout cần xác nhận tổng mới", async () =>
        {
            var f = await Fixture.Create();
            f.Controller.AddItemToCart("plain", 2, []);
            f.Controller.ApplyPromotion("SALE10");
            f.Source.Data = f.Source.Data with { Promotions = f.Source.Data.Promotions.Select(x => x with { Active = false }).ToImmutableArray() };
            Equal(CheckoutResultKind.Rejected, (await f.Controller.Checkout(new(PaymentMethod.Cash, 50000))).Kind);
            Equal(50000m, f.Controller.Totals.Payable);
            Assert(f.Controller.Promotion is null, "Mã đã bỏ.");
            Equal(0, f.Orders.Values.Count);
        });
        Add("TC17 Snapshot hóa đơn không đổi sau thay đổi danh mục", async () =>
        {
            var f = await Fixture.Create();
            f.Controller.AddItemToCart("options", 1, [new("size", "L")]);
            var paid = (await f.Controller.Checkout(new(PaymentMethod.Cash, 50000))).Receipt!;
            f.Source.Data = f.Source.Data with { Menu = [] };
            var text = ReceiptFormatter.Format(paid);
            Assert(text.Contains("45.000") && text.Contains("L"), "Hóa đơn dùng giá/tùy chọn đã bán.");
            Throws(() => f.Controller.RemoveCartItem(paid.Lines[0].LineId));
            Equal(1, f.Orders.Values.Count);
        });
        Add("TC18 Lưu JSON thật, đọc lại và ngăn ghi đè/trùng", async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "cashier-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var f = await Fixture.Create();
                f.Controller.AddItemToCart("options", 1, [new("size", "L"), new("top", "pearl")]);
                var receipt = (await f.Controller.Checkout(new(PaymentMethod.Cash, 60000))).Receipt!;
                var repository = new JsonOrderRepository(directory);
                await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => repository.SaveAsync(receipt)));
                Equal(1, Directory.GetFiles(directory, "*.json").Length);
                var read = (await repository.FindAsync(receipt.OrderId))!;
                Assert(ReceiptIdentity.Same(read, receipt), "Các tùy chọn/giá/tiền phải được giữ sau serialize.");
                await ThrowsAsync(() => repository.SaveAsync(receipt with { StaffName = "Khác" }));
            }
            finally { Directory.Delete(directory, recursive: true); }
        });
        Add("TC19 Checkout qua kho JSON và journal thật", async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "cashier-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var source = new FakeSource(SampleData());
                var repo = new JsonOrderRepository(Path.Combine(directory, "orders"));
                var journal = new JsonCheckoutJournal(Path.Combine(directory, "pending.json"));
                var c = new CashierController(source, repo, journal, () => Now);
                await c.InitializeAsync();
                c.AddItemToCart("options", 2, [new("size", "L")]);
                var result = await c.Checkout(new(PaymentMethod.Cash, 100000));
                Equal(CheckoutResultKind.Paid, result.Kind);
                Assert(!File.Exists(Path.Combine(directory, "pending.json")), "Journal được dọn sau lưu.");
                Assert(ReceiptFormatter.Format(result.Receipt!).Contains("90.000"), "Tổng hóa đơn đúng.");
            }
            finally { Directory.Delete(directory, recursive: true); }
        });
        Add("TC20 Số lượng sai, bỏ giỏ cần xác nhận", async () =>
        {
            var f = await Fixture.Create();
            foreach (var quantity in new[] { 0, -1, 1000 }) Throws(() => f.Controller.AddItemToCart("plain", quantity, []));
            f.Controller.AddItemToCart("plain", 1, []);
            Throws(() => f.Controller.StartNewOrder());
            f.Controller.StartNewOrder(true);
            Equal(0, f.Controller.Lines.Count);
        });
        Add("TC21 Chặn tiền lẻ VND và phương thức không hợp lệ", async () =>
        {
            var f = await Fixture.Create();
            f.Controller.AddItemToCart("plain", 1, []);
            Equal(CheckoutResultKind.Rejected, (await f.Controller.Checkout(new(PaymentMethod.Cash, 30000.5m))).Kind);
            Equal(CheckoutResultKind.Rejected, (await f.Controller.Checkout(new((PaymentMethod)99, 30000))).Kind);
        });
        Add("TC22 Mở lại chỉ khi xác định chưa lưu", async () =>
        {
            var f = await Fixture.Create();
            f.Orders.FailBeforeCommit = true;
            f.Controller.AddItemToCart("plain", 1, []);
            await f.Controller.Checkout(new(PaymentMethod.Cash, 30000));
            f.Orders.FailReads = true;
            await ThrowsAsync(() => f.Controller.UnlockUnpaidOrderAsync());
            Assert(!f.Controller.CanEdit, "Không cho mở giỏ khi chưa xác minh.");
            f.Orders.FailReads = false;
            await f.Controller.UnlockUnpaidOrderAsync();
            Assert(f.Controller.CanEdit && f.Controller.PendingReceipt is null, "Mở lại giỏ chưa lưu.");
        });
        Add("TC23 Dữ liệu DemoCatalog được đọc bằng adapter thật", async () =>
        {
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Cashier.WinForms/Data/DemoCatalog.json"));
            var data = await new JsonSalesDataSource(path, new DemoSessionProvider()).ReadAsync();
            Equal(10, data.Menu.Length);
            Assert(data.Session.IsDemo && data.Promotions.Any(x => x.Code == "SALE10"), "Danh mục mẫu hợp lệ.");
        });
        Add("TC24 Journal lỗi ngăn ghi đơn trước khi có bản chờ", async () =>
        {
            var source = new FakeSource(SampleData());
            var repo = new FakeOrders();
            var c = new CashierController(source, repo, new FailingJournal(), () => Now);
            await c.InitializeAsync();
            c.AddItemToCart("plain", 1, []);
            Equal(CheckoutResultKind.Rejected, (await c.Checkout(new(PaymentMethod.Cash, 30000))).Kind);
            Equal(0, repo.SaveCalls);
            Assert(c.CanEdit, "Không ghi đơn khi journal lỗi.");
        });
        Add("TC25 Lỗi đường dẫn đọc không được coi là chưa có tệp", async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "cashier-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                // A directory at the file path makes File.Exists return false, but opening it must fail.
                await ThrowsAsync(() => BusinessJson.ReadAsync<Receipt>(directory));
                Assert(await BusinessJson.ReadAsync<Receipt>(Path.Combine(directory, "missing.json")) is null,
                    "Tệp thực sự không tồn tại vẫn trả NOT FOUND.");
            }
            finally { Directory.Delete(directory, recursive: true); }
        });
        Add("TC26 Chưa tìm thấy đơn từ xa không cho bỏ mã khi ghi có thể đến muộn", async () =>
        {
            var f = await Fixture.Create();
            f.Orders.FailBeforeCommit = true;
            f.Orders.ReleaseAllowed = false;
            f.Controller.AddItemToCart("plain", 1, []);
            await f.Controller.Checkout(new(PaymentMethod.Cash, 30000));
            var pending = f.Controller.PendingReceipt!;
            Assert(!f.Controller.CanUnlockUnpaidOrder, "Chưa được bỏ giao dịch đang chờ.");
            await ThrowsAsync(() => f.Controller.UnlockUnpaidOrderAsync());
            Throws(() => f.Controller.StartNewOrder(true));
            f.Orders.Values.Add(pending.OrderId, pending); // Simulate the original remote write completing later.
            var result = await f.Controller.ResolvePendingAsync();
            Equal(CheckoutResultKind.Paid, result.Kind);
            Equal(pending.OrderId, result.Receipt!.OrderId);
            Equal(1, f.Orders.Values.Count);
        });
        Add("TC27 Hóa đơn có tiền lẻ VND bị từ chối", async () =>
        {
            var f = await Fixture.Create();
            f.Controller.AddItemToCart("plain", 1, []);
            var receipt = (await f.Controller.Checkout(new(PaymentMethod.Cash, 30000))).Receipt!;
            var invalid = receipt with
            {
                Lines = [receipt.Lines[0] with { BasePrice = 25000.5m }],
                Totals = new(25000.5m, 0, 25000.5m),
                Payment = new(PaymentMethod.Cash, 30000, 4999.5m)
            };
            Throws(() => CartRules.ValidateReceipt(invalid));
        });
        Add("TC28 Hóa đơn giảm giá không khớp snapshot chính sách bị từ chối", async () =>
        {
            var f = await Fixture.Create();
            f.Controller.AddItemToCart("plain", 2, []);
            f.Controller.ApplyPromotion("SALE10");
            var receipt = (await f.Controller.Checkout(new(PaymentMethod.Cash, 60000))).Receipt!;
            var invalid = receipt with
            {
                Promotion = receipt.Promotion! with { Discount = 6000 },
                Totals = new(50000, 6000, 44000),
                Payment = new(PaymentMethod.Cash, 60000, 16000)
            };
            Throws(() => CartRules.ValidateReceipt(invalid));
        });
        Add("TC29 Hóa đơn trùng mã dòng hoặc thiếu tùy chọn bị từ chối", async () =>
        {
            var f = await Fixture.Create();
            f.Controller.AddItemToCart("plain", 1, []);
            var receipt = (await f.Controller.Checkout(new(PaymentMethod.Cash, 30000))).Receipt!;
            Throws(() => CartRules.ValidateReceipt(receipt with { Lines = [receipt.Lines[0], receipt.Lines[0]] }));
            Throws(() => CartRules.ValidateReceipt(receipt with { Lines = [receipt.Lines[0] with { Options = default }] }));
        });
        Add("TC30 Không nạp lặp giao dịch trong cùng controller", async () =>
        {
            var f = await Fixture.Create();
            f.Controller.AddItemToCart("plain", 1, []);
            await ThrowsAsync(() => f.Controller.InitializeAsync());
            Equal(1, f.Controller.Lines.Count);
        });
        Add("TC31 Cấu hình giảm cố định tiền lẻ hoặc lựa chọn trùng bị từ chối", async () =>
        {
            var promotion = new Promotion("FRACTION", "Mẫu", DiscountKind.FixedAmount, 0.5m, 0,
                Now.AddDays(-1), Now.AddDays(1), true);
            Throws(() => CartRules.ApplyPromotion(promotion, 25000, Now));
            var item = new MenuItem("duplicate", "Món", "Mẫu", 10000, true,
                [new("size", "Size", 1, 1, [new("M", "M", 0), new("M", "Khác", 5000)])]);
            Throws(() => CartRules.ApplyOption(item, [new("size", "M")]));
            await Task.CompletedTask;
        });
        Add("TC32 Hóa đơn JSON hỏng không được coi là không có giao dịch", async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "cashier-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var id = Guid.NewGuid();
                await File.WriteAllTextAsync(Path.Combine(directory, id.ToString("N") + ".json"), "not-json");
                var repo = new JsonOrderRepository(directory);
                await ThrowsAsync(() => repo.FindAsync(id));
                await ThrowsAsync(() => repo.CanReleaseUnpaidAsync(id));
            }
            finally { Directory.Delete(directory, recursive: true); }
        });
        Add("TC33 Tiền mặt lớn và phiên không được bán", async () =>
        {
            var payment = CartRules.ValidatePayment(new(PaymentMethod.Cash, 2_000_000_000), 1_500_000_000);
            Equal(500_000_000m, payment.Change);
            var source = new FakeSource(SampleData() with { Session = SampleData().Session with { CanSell = false } });
            var c = new CashierController(source, new FakeOrders(), new MemoryJournal(), () => Now);
            await c.InitializeAsync();
            Assert(!c.CanEdit, "Phiên không có quyền bán không được sửa giỏ.");
            Throws(() => c.AddItemToCart("plain", 1, []));
        });

        Add("TC34 Thêm lặp món thường gộp vào cùng dòng", async () =>
        {
            var f = await Fixture.Create();
            var a = f.Controller.AddItemToCart("plain", 1, []);
            var b = f.Controller.AddItemToCart("plain", 1, []);
            Equal(a.LineId, b.LineId);
            Equal(1, f.Controller.Lines.Count);
            Equal(2, b.Quantity);
            Equal(50000m, f.Controller.Totals.Payable);
        });
        Add("TC35 Cùng tùy chọn, khác thứ tự chọn vẫn gộp", async () =>
        {
            var f = await Fixture.Create();
            var a = f.Controller.AddItemToCart("options", 1, [new("size", "L"), new("top", "pearl")]);
            var b = f.Controller.AddItemToCart("options", 2, [new("top", "pearl"), new("size", "L")]);
            Equal(a.LineId, b.LineId);
            Equal(1, f.Controller.Lines.Count);
            Equal(3, b.Quantity);
            Equal(150000m, f.Controller.Totals.Payable);
        });
        Add("TC36 Khác topping giữ hai dòng độc lập", async () =>
        {
            var f = await Fixture.Create();
            var a = f.Controller.AddItemToCart("options", 1, [new("size", "M")]);
            var b = f.Controller.AddItemToCart("options", 1, [new("size", "M"), new("top", "pearl")]);
            Equal(2, f.Controller.Lines.Count);
            Assert(a.LineId != b.LineId, "Khác topping không được gộp.");
            Equal(85000m, f.Controller.Totals.Payable);
        });
        Add("TC37 Sửa thành cùng tùy chọn thì gộp, giữ dòng đang sửa", async () =>
        {
            var f = await Fixture.Create();
            var a = f.Controller.AddItemToCart("options", 2, [new("size", "M")]);
            f.Controller.AddItemToCart("options", 3, [new("size", "L")]);
            f.Controller.UpdateCartItem(a.LineId, 2, [new("size", "L")]);
            Equal(1, f.Controller.Lines.Count);
            Equal(a.LineId, f.Controller.Lines[0].LineId);
            Equal(5, f.Controller.Lines[0].Quantity);
            Equal(225000m, f.Controller.Totals.Payable);
        });
        Add("TC38 Gộp vượt 999 giữ nguyên giỏ và ưu đãi", async () =>
        {
            var f = await Fixture.Create();
            f.Controller.AddItemToCart("plain", 999, []);
            f.Controller.ApplyPromotion("SALE10");
            var before = f.Controller.Totals;
            var promo = f.Controller.Promotion;
            Throws(() => f.Controller.AddItemToCart("plain", 1, []));
            Equal(1, f.Controller.Lines.Count);
            Equal(999, f.Controller.Lines[0].Quantity);
            Equal(before, f.Controller.Totals);
            Equal(promo, f.Controller.Promotion);
        });
        Add("TC39 Sửa để gộp vượt 999 không mất dòng nào", async () =>
        {
            var f = await Fixture.Create();
            var a = f.Controller.AddItemToCart("options", 999, [new("size", "M")]);
            var b = f.Controller.AddItemToCart("options", 1, [new("size", "L")]);
            var before = f.Controller.Totals;
            Throws(() => f.Controller.UpdateCartItem(b.LineId, 1, [new("size", "M")]));
            Equal(2, f.Controller.Lines.Count);
            Equal(a, f.Controller.Lines[0]);
            Equal(b, f.Controller.Lines[1]);
            Equal(before, f.Controller.Totals);
        });
        Add("TC40 Khác snapshot giá không được gộp", async () =>
        {
            var f = await Fixture.Create();
            f.Controller.AddItemToCart("plain", 1, []);
            f.Source.Data = f.Source.Data with
            {
                Menu = f.Source.Data.Menu.Select(x => x.Id == "plain" ? x with { BasePrice = 30000 } : x).ToImmutableArray()
            };
            await f.Controller.RefreshCatalogAsync();
            f.Controller.AddItemToCart("plain", 1, []);
            Equal(2, f.Controller.Lines.Count);
            Equal(55000m, f.Controller.Totals.Payable);
            Equal(CheckoutResultKind.Rejected, (await f.Controller.Checkout(new(PaymentMethod.Cash, 60000))).Kind);
            Equal(0, f.Orders.Values.Count);
        });
        Add("TC41 Giỏ rỗng không tạo hóa đơn hoặc bản chờ", async () =>
        {
            var f = await Fixture.Create();
            Equal(CheckoutResultKind.Rejected, (await f.Controller.Checkout(new(PaymentMethod.Cash, 0))).Kind);
            Equal(CheckoutState.Ready, f.Controller.State);
            Equal(0, f.Orders.SaveCalls);
            Assert(f.Journal.Candidate is null, "Giỏ rỗng không được ghi bản chờ.");
        });
        Add("TC42 Gộp món bỏ mã để áp dụng lại theo giỏ mới", async () =>
        {
            var f = await Fixture.Create();
            var a = f.Controller.AddItemToCart("plain", 2, []);
            f.Controller.ApplyPromotion("SALE10");
            var b = f.Controller.AddItemToCart("plain", 1, []);
            Equal(a.LineId, b.LineId);
            Equal(3, b.Quantity);
            Assert(f.Controller.Promotion is null, "Giỏ thay đổi phải kiểm tra lại mã.");
            Equal(75000m, f.Controller.Totals.Payable);
            f.Controller.ApplyPromotion("SALE10");
            Equal(67500m, f.Controller.Totals.Payable);
        });


        Add("TC43 Lịch sử JSON sắp mới nhất trước và giới hạn số đơn", async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "cashier-history-" + Guid.NewGuid().ToString("N"));
            try
            {
                var repo = new JsonOrderRepository(directory);
                var example = await PaidExample();
                var a = example with { OrderId = Guid.NewGuid(), ReceiptNumber = "HD-A", PaidAt = Now.AddDays(-3) };
                var b = example with { OrderId = Guid.NewGuid(), ReceiptNumber = "HD-B", PaidAt = Now.AddDays(-2) };
                var c = example with { OrderId = Guid.NewGuid(), ReceiptNumber = "HD-C", PaidAt = Now.AddDays(-1) };
                await repo.SaveAsync(a); await repo.SaveAsync(c); await repo.SaveAsync(b);
                var history = await repo.ReadRecentAsync(2);
                Equal(2, history.Receipts.Length);
                Equal(c.OrderId, history.Receipts[0].OrderId);
                Equal(b.OrderId, history.Receipts[1].OrderId);
                Equal(0, history.UnreadableFiles);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
        Add("TC44 Tệp lịch sử hỏng/sai mã được báo, không xóa hay sửa hóa đơn", async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "cashier-history-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var repo = new JsonOrderRepository(directory);
                var example = await PaidExample();
                await repo.SaveAsync(example);
                var original = Path.Combine(directory, example.OrderId.ToString("N") + ".json");
                var before = await File.ReadAllTextAsync(original);
                var corrupt = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
                var mismatch = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
                await File.WriteAllTextAsync(corrupt, "{bad json");
                await File.WriteAllTextAsync(mismatch, before);
                var history = await repo.ReadRecentAsync();
                Equal(1, history.Receipts.Length);
                Equal(2, history.UnreadableFiles);
                Assert(ReceiptIdentity.Same(example, history.Receipts[0]), "Hóa đơn phải giữ nguyên.");
                Equal(before, await File.ReadAllTextAsync(original));
                Assert(File.Exists(corrupt) && File.Exists(mismatch), "Không được xóa tệp lỗi.");
            }
            finally { Directory.Delete(directory, true); }
        });
        Add("TC45 Chưa có lịch sử, giới hạn sai và hủy đọc không tạo dữ liệu", async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "cashier-history-" + Guid.NewGuid().ToString("N"));
            var repo = new JsonOrderRepository(directory);
            var history = await repo.ReadRecentAsync();
            Equal(0, history.Receipts.Length);
            Equal(0, history.UnreadableFiles);
            await ThrowsAsync(async () => { await repo.ReadRecentAsync(0); });
            await ThrowsAsync(async () => { await repo.ReadRecentAsync(501); });
            await ThrowsAsync(async () => { await repo.ReadRecentAsync(token: new CancellationToken(true)); });
            Assert(!Directory.Exists(directory), "Đọc lịch sử không được tự ghi dữ liệu.");
        });
        Add("TC46 Tạo đơn tương tự dùng giá mới, không sao chép ưu đãi/tiền cũ", async () =>
        {
            var f = await Fixture.Create();
            var old = await PaidExample();
            f.Source.Data = f.Source.Data with
            {
                Menu = f.Source.Data.Menu.Select(x => x.Id == "plain" ? x with { BasePrice = 30000 } : x).ToImmutableArray()
            };
            await f.Controller.CreateSimilarOrderAsync(old);
            Equal(CheckoutState.Ready, f.Controller.State);
            Equal(2, f.Controller.Lines.Count);
            Equal(110000m, f.Controller.Totals.Payable);
            Assert(f.Controller.Promotion is null && f.Controller.PaidReceipt is null &&
                f.Controller.PendingReceipt is null, "Phải là giỏ mới chưa thanh toán.");
            Assert(f.Controller.Lines.All(x => old.Lines.All(y => y.LineId != x.LineId)), "Dòng mới phải có mã mới.");
            Equal(0, f.Orders.SaveCalls);
            Assert(f.Journal.Candidate is null, "Chưa thanh toán không ghi bản chờ.");
            Equal(90000m, old.Totals.Payable);
            Equal(200000m, old.Payment.AmountReceived);
        });
        Add("TC47 Giỏ có món cần xác nhận trước khi tạo đơn tương tự", async () =>
        {
            var f = await Fixture.Create();
            var line = f.Controller.AddItemToCart("ninety-nine", 1, []);
            f.Controller.ApplyPromotion("SALE10");
            var total = f.Controller.Totals;
            var promotion = f.Controller.Promotion;
            var old = await PaidExample();
            await ThrowsAsync(() => f.Controller.CreateSimilarOrderAsync(old));
            Equal(1, f.Controller.Lines.Count);
            Equal(line, f.Controller.Lines[0]);
            Equal(total, f.Controller.Totals);
            Equal(promotion, f.Controller.Promotion);
            await f.Controller.CreateSimilarOrderAsync(await PaidExample(), true);
            Equal(100000m, f.Controller.Totals.Payable);
            Assert(f.Controller.Promotion is null, "Đơn mới không giữ mã của giỏ cũ.");
        });
        Add("TC48 Một món ngừng bán giữ nguyên toàn bộ giỏ và ưu đãi", async () =>
        {
            var f = await Fixture.Create();
            var line = f.Controller.AddItemToCart("plain", 3, []);
            f.Controller.ApplyPromotion("SALE10");
            var total = f.Controller.Totals;
            var promotion = f.Controller.Promotion;
            f.Source.Data = f.Source.Data with
            {
                Menu = f.Source.Data.Menu.Select(x => x.Id == "options" ? x with { Available = false } : x).ToImmutableArray()
            };
            var old = await PaidExample();
            await ThrowsAsync(() => f.Controller.CreateSimilarOrderAsync(old, true));
            Equal(1, f.Controller.Lines.Count);
            Equal(line, f.Controller.Lines[0]);
            Equal(total, f.Controller.Totals);
            Equal(promotion, f.Controller.Promotion);
            Equal(0, f.Orders.SaveCalls);
        });
        Add("TC49 Tùy chọn đã gỡ không được tạo lại, giữ giỏ hiện tại", async () =>
        {
            var f = await Fixture.Create();
            var line = f.Controller.AddItemToCart("plain", 1, []);
            f.Source.Data = f.Source.Data with
            {
                Menu = f.Source.Data.Menu.Select(x => x.Id == "options" ? x with
                {
                    OptionGroups = x.OptionGroups.Select(g => g.Id == "size" ? g with
                    { Choices = g.Choices.Where(c => c.Id != "L").ToImmutableArray() } : g).ToImmutableArray()
                } : x).ToImmutableArray()
            };
            var old = await PaidExample();
            await ThrowsAsync(() => f.Controller.CreateSimilarOrderAsync(old, true));
            Equal(line, f.Controller.Lines.Single());
            Assert(f.Journal.Candidate is null, "Không tạo bản chờ cho đơn không hợp lệ.");
        });
        Add("TC50 Thanh toán đơn tương tự có mã mới, phiên và tiền nhận mới", async () =>
        {
            var f = await Fixture.Create();
            var old = await PaidExample();
            f.Source.Data = f.Source.Data with
            {
                Session = new("STAFF-NEW", "Thu ngân hiện tại", "SHIFT-NEW", true, true, true)
            };
            await f.Controller.CreateSimilarOrderAsync(old);
            Equal(0, f.Orders.SaveCalls);
            var paid = await f.Controller.Checkout(new(PaymentMethod.Cash, 120000));
            Equal(CheckoutResultKind.Paid, paid.Kind);
            Assert(paid.Receipt!.OrderId != old.OrderId, "Không dùng lại mã đơn đã thanh toán.");
            Equal(Now, paid.Receipt.PaidAt);
            Equal("STAFF-NEW", paid.Receipt.StaffId);
            Equal("SHIFT-NEW", paid.Receipt.ShiftId);
            Equal(120000m, paid.Receipt.Payment.AmountReceived);
            Equal(20000m, paid.Receipt.Payment.Change);
            Assert(paid.Receipt.Promotion is null, "Không mang ưu đãi cũ.");
            Equal(1, f.Orders.SaveCalls);
        });
        Add("TC51 Giao dịch đang chờ chặn tạo đơn tương tự", async () =>
        {
            var f = await Fixture.Create();
            f.Controller.AddItemToCart("plain", 1, []);
            f.Orders.FailBeforeCommit = true;
            f.Orders.FailReads = true;
            await f.Controller.Checkout(new(PaymentMethod.Cash, 30000));
            var pending = f.Controller.PendingReceipt;
            var old = await PaidExample();
            await ThrowsAsync(() => f.Controller.CreateSimilarOrderAsync(old, true));
            Equal(CheckoutState.Uncertain, f.Controller.State);
            Equal(pending, f.Controller.PendingReceipt);
            Equal(pending, f.Journal.Candidate);
            Equal(1, f.Controller.Lines.Count);
        });
        Add("TC52 Giá cũ khác nhau nay cùng cấu hình được gộp trong giỏ mới", async () =>
        {
            var f = await Fixture.Create();
            var example = await PaidExample();
            var lines = ImmutableArray.Create(
                new CartLine(Guid.NewGuid(), "plain", "Cà phê đen", 25000, [], 2),
                new CartLine(Guid.NewGuid(), "plain", "Cà phê đen", 30000, [], 3));
            var subtotal = lines.Sum(x => x.LineTotal);
            var old = example with { Lines = lines, Promotion = null, Totals = new(subtotal, 0, subtotal),
                Payment = new(PaymentMethod.Cash, subtotal, 0) };
            await f.Controller.CreateSimilarOrderAsync(old);
            Equal(1, f.Controller.Lines.Count);
            Equal(5, f.Controller.Lines[0].Quantity);
            Equal(125000m, f.Controller.Totals.Payable);
        });
        Add("TC53 Gộp đơn tương tự vượt 999 không làm mất giỏ cũ", async () =>
        {
            var f = await Fixture.Create();
            var current = f.Controller.AddItemToCart("plain", 1, []);
            var example = await PaidExample();
            var lines = ImmutableArray.Create(
                new CartLine(Guid.NewGuid(), "plain", "Cà phê đen", 25000, [], 999),
                new CartLine(Guid.NewGuid(), "plain", "Cà phê đen", 30000, [], 1));
            var subtotal = lines.Sum(x => x.LineTotal);
            var old = example with { Lines = lines, Promotion = null, Totals = new(subtotal, 0, subtotal),
                Payment = new(PaymentMethod.Cash, subtotal, 0) };
            await ThrowsAsync(() => f.Controller.CreateSimilarOrderAsync(old, true));
            Equal(current, f.Controller.Lines.Single());
            Equal(25000m, f.Controller.Totals.Payable);
        });
        Add("TC54 Phiên/ca mới không hợp lệ chặn tạo giỏ tương tự", async () =>
        {
            var f = await Fixture.Create();
            var current = f.Controller.AddItemToCart("plain", 1, []);
            f.Source.Data = f.Source.Data with { Session = f.Source.Data.Session with { ShiftOpen = false } };
            var old = await PaidExample();
            await ThrowsAsync(() => f.Controller.CreateSimilarOrderAsync(old, true));
            Equal(current, f.Controller.Lines.Single());
            Equal(0, f.Orders.SaveCalls);
        });
        Add("TC55 JSON thật: xem/tạo giỏ không ghi đơn, checkout mới không sửa đơn gốc", async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "cashier-history-" + Guid.NewGuid().ToString("N"));
            try
            {
                var receipts = Path.Combine(directory, "receipts");
                var repo = new JsonOrderRepository(receipts);
                var journalPath = Path.Combine(directory, "pending-checkout.json");
                var controller = new CashierController(new FakeSource(SampleData()), repo,
                    new JsonCheckoutJournal(journalPath), () => Now);
                var old = await PaidExample();
                await repo.SaveAsync(old);
                var original = Path.Combine(receipts, old.OrderId.ToString("N") + ".json");
                var before = await File.ReadAllTextAsync(original);
                await controller.InitializeAsync();
                var history = await repo.ReadRecentAsync();
                await controller.CreateSimilarOrderAsync(history.Receipts.Single());
                Equal(1, Directory.GetFiles(receipts, "*.json").Length);
                Assert(!File.Exists(journalPath), "Tạo giỏ không ghi bản chờ.");
                Equal(before, await File.ReadAllTextAsync(original));
                var paid = await controller.Checkout(new(PaymentMethod.Cash, 100000));
                Equal(CheckoutResultKind.Paid, paid.Kind);
                Assert(paid.Receipt!.OrderId != old.OrderId, "Thanh toán phải là đơn mới.");
                Equal(2, Directory.GetFiles(receipts, "*.json").Length);
                Equal(before, await File.ReadAllTextAsync(original));
                Equal(2, (await repo.ReadRecentAsync()).Receipts.Length);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });

        Add("TC56 Phiên đóng khi khởi tạo được tải lại sau khi mở ca", async () =>
        {
            var source = new FakeSource(SampleData());
            source.Data = source.Data with { Session = source.Data.Session with { ShiftOpen = false, CanSell = false } };
            var controller = new CashierController(source, new FakeOrders(), new MemoryJournal(), () => Now);
            await ThrowsAsync(() => controller.RefreshCatalogAsync());
            await controller.InitializeAsync();
            Assert(!controller.CanEdit, "Phiên đóng chưa cho bán.");
            source.Data = source.Data with { Session = source.Data.Session with { ShiftOpen = true, CanSell = true } };
            await controller.RefreshCatalogAsync();
            Assert(controller.CanEdit, "Tải lại phiên phải nhận ca/quyền vừa được mở.");
            controller.AddItemToCart("plain", 1, []);
            Equal(CheckoutResultKind.Paid, (await controller.Checkout(new(PaymentMethod.Cash, 30000))).Kind);
        });
        Add("TC57 Checkout gặp ca đóng vẫn giữ giỏ/mã và tải lại để bán tiếp", async () =>
        {
            var f = await Fixture.Create();
            var line = f.Controller.AddItemToCart("plain", 2, []);
            f.Controller.ApplyPromotion("SALE10");
            f.Source.Data = f.Source.Data with { Session = f.Source.Data.Session with { ShiftOpen = false } };
            Equal(CheckoutResultKind.Rejected, (await f.Controller.Checkout(new(PaymentMethod.Cash, 50000))).Kind);
            Assert(!f.Controller.CanEdit, "Phiên đóng phải khóa sửa/bán.");
            f.Source.Data = f.Source.Data with { Session = f.Source.Data.Session with { ShiftOpen = true } };
            await f.Controller.RefreshCatalogAsync();
            Assert(f.Controller.CanEdit, "Tải lại phải khôi phục khả năng bán.");
            Equal(line.LineId, f.Controller.Lines.Single().LineId);
            Equal("SALE10", f.Controller.Promotion!.Code);
            Equal(45000m, f.Controller.Totals.Payable);
            Equal(CheckoutResultKind.Paid, (await f.Controller.Checkout(new(PaymentMethod.Cash, 50000))).Kind);
            Equal(1, f.Orders.Values.Count);
        });
        Add("TC58 Tải lại không thay giao dịch chờ hoặc hóa đơn đã thanh toán", async () =>
        {
            var f = await Fixture.Create();
            f.Controller.AddItemToCart("plain", 1, []);
            f.Orders.FailBeforeCommit = true;
            Equal(CheckoutResultKind.NotSaved, (await f.Controller.Checkout(new(PaymentMethod.Cash, 30000))).Kind);
            var pending = f.Controller.PendingReceipt!;
            await ThrowsAsync(() => f.Controller.RefreshCatalogAsync());
            Equal(CheckoutState.NotSaved, f.Controller.State);
            Assert(ReceiptIdentity.Same(pending, f.Controller.PendingReceipt!), "Tải lại không được thay bản chờ.");
            f.Orders.FailReads = true;
            Equal(CheckoutResultKind.Uncertain, (await f.Controller.ResolvePendingAsync()).Kind);
            await ThrowsAsync(() => f.Controller.RefreshCatalogAsync());
            Equal(CheckoutState.Uncertain, f.Controller.State);
            Assert(ReceiptIdentity.Same(pending, f.Controller.PendingReceipt!), "Bản chờ chưa rõ kết quả phải giữ nguyên.");
            f.Orders.FailReads = f.Orders.FailBeforeCommit = false;
            Equal(CheckoutResultKind.Paid, (await f.Controller.Checkout(new(PaymentMethod.Cash, 0))).Kind);
            await ThrowsAsync(() => f.Controller.RefreshCatalogAsync());
            Equal(CheckoutState.Paid, f.Controller.State);
            Assert(ReceiptIdentity.Same(pending, f.Controller.PaidReceipt!), "Hóa đơn trả tiền giữ nguyên nội dung.");
            Equal(1, f.Orders.Values.Count);
        });

        Console.WriteLine("Cashier automated business/storage tests — " + DateTimeOffset.UtcNow.ToString("O"));
        var failed = 0;
        foreach (var test in Cases)
        {
            try { await test.Run(); Console.WriteLine("PASS | " + test.Name); }
            catch (Exception e) { failed++; Console.WriteLine("FAIL | " + test.Name + " | " + e.Message); }
        }
        Console.WriteLine($"RESULT: {Cases.Count - failed}/{Cases.Count} passed, {failed} failed.");
        Console.WriteLine("These tests do not run the Windows UI, a physical printer, or the group's MongoDB.");
        return failed == 0 ? 0 : 1;
    }
    private static void Add(string name, Func<Task> run) => Cases.Add((name, run));
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual) => Assert(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}, actual {actual}");
    private static void Throws(Action action)
    {
        try { action(); } catch (BusinessException) { return; }
        throw new Exception("Expected BusinessException.");
    }
    private static async Task ThrowsAsync(Func<Task> action)
    {
        try { await action(); } catch (Exception) { return; }
        throw new Exception("Expected exception.");
    }
    private static SalesData SampleData() => new([
        new("plain", "Cà phê đen", "Cà phê", 25000, true, []),
        new("ninety-nine", "Món 99k", "Mẫu", 99000, true, []),
        new("off", "Tạm hết", "Mẫu", 10000, false, []),
        new("options", "Macchiato", "Cà phê", 40000, true, [
            new("size", "Kích cỡ", 1, 1, [new("M", "M", 0), new("L", "L", 5000)]),
            new("top", "Topping", 0, 1, [new("pearl", "Trân châu", 5000)])])
        ], [
        new("SALE10", "Giảm 10%", DiscountKind.Percentage, 10, 40000, Now.AddDays(-1), Now.AddDays(1), true),
        new("EXPIRED", "Hết hạn", DiscountKind.Percentage, 10, 0, Now.AddDays(-10), Now.AddDays(-1), true),
        new("BIG", "Giảm lớn", DiscountKind.FixedAmount, 100000, 0, Now.AddDays(-1), Now.AddDays(1), true)
        ], new("DEMO-KHOI", "Nguyễn Tuấn Khôi", "DEMO-CA", true, true, true));


    private static async Task<Receipt> PaidExample()
    {
        var f = await Fixture.Create();
        f.Controller.AddItemToCart("plain", 2, []);
        f.Controller.AddItemToCart("options", 1, [new("size", "L"), new("top", "pearl")]);
        f.Controller.ApplyPromotion("SALE10");
        var paid = await f.Controller.Checkout(new(PaymentMethod.Cash, 200000));
        if (paid.Kind != CheckoutResultKind.Paid) throw new Exception(paid.Message);
        return paid.Receipt! with { PaidAt = Now.AddDays(-1) };
    }

    private sealed record Fixture(CashierController Controller, FakeSource Source, FakeOrders Orders, MemoryJournal Journal)
    {
        public static async Task<Fixture> Create()
        {
            var source = new FakeSource(SampleData());
            var orders = new FakeOrders();
            var journal = new MemoryJournal();
            var controller = new CashierController(source, orders, journal, () => Now);
            await controller.InitializeAsync();
            return new(controller, source, orders, journal);
        }
    }
    private sealed class FakeSource(SalesData initial) : ICashierDataSource
    {
        public SalesData Data { get; set; } = initial;
        public Task<SalesData> ReadAsync(CancellationToken token = default) => Task.FromResult(Data);
    }
    private sealed class FakeOrders : IOrderRepository
    {
        public Dictionary<Guid, Receipt> Values { get; } = [];
        public bool FailBeforeCommit { get; set; }
        public bool FailAfterCommit { get; set; }
        public bool FailReads { get; set; }
        public bool ReleaseAllowed { get; set; } = true;
        public int SaveCalls { get; private set; }
        public Task<Receipt?> FindAsync(Guid id, CancellationToken token = default)
        {
            if (FailReads) throw new IOException("Storage unavailable");
            return Task.FromResult(Values.GetValueOrDefault(id));
        }
        public async Task<bool> CanReleaseUnpaidAsync(Guid id, CancellationToken token = default) =>
            ReleaseAllowed && await FindAsync(id, token) is null;
        public Task<Receipt> SaveAsync(Receipt value, CancellationToken token = default)
        {
            SaveCalls++;
            if (FailBeforeCommit) throw new IOException("Write failed before commit");
            Values.TryAdd(value.OrderId, value);
            if (FailAfterCommit) throw new IOException("Connection lost after commit");
            return Task.FromResult(Values[value.OrderId]);
        }
    }
    private sealed class MemoryJournal : ICheckoutJournal
    {
        public Receipt? Candidate { get; private set; }
        public Task<Receipt?> ReadAsync(CancellationToken token = default) => Task.FromResult(Candidate);
        public Task WriteAsync(Receipt receipt, CancellationToken token = default) { Candidate = receipt; return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken token = default) { Candidate = null; return Task.CompletedTask; }
    }
    private sealed class FailingJournal : ICheckoutJournal
    {
        public Task<Receipt?> ReadAsync(CancellationToken token = default) => Task.FromResult<Receipt?>(null);
        public Task WriteAsync(Receipt receipt, CancellationToken token = default) => throw new IOException("Journal unavailable");
        public Task ClearAsync(CancellationToken token = default) => Task.CompletedTask;
    }
}
