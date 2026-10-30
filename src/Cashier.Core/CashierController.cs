using System.Collections.Immutable;

namespace Cashier.Core;

public sealed class CashierController(ICashierDataSource source, IOrderRepository orders,
    ICheckoutJournal journal, Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> _now = clock ?? (() => DateTimeOffset.Now);
    private readonly SemaphoreSlim _checkoutGate = new(1, 1);
    private readonly List<CartLine> _lines = [];
    private Receipt? _candidate;
    private bool _initialized;
    private bool _canReleaseUnpaid;
    public SalesData Data { get; private set; } = null!;
    public IReadOnlyList<CartLine> Lines => _lines.AsReadOnly();
    public AppliedPromotion? Promotion { get; private set; }
    public CartTotals Totals => new(_lines.Sum(x => x.LineTotal), Promotion?.Discount ?? 0,
        _lines.Sum(x => x.LineTotal) - (Promotion?.Discount ?? 0));
    public CheckoutState State { get; private set; } = CheckoutState.Ready;
    public Receipt? PaidReceipt { get; private set; }
    public Receipt? PendingReceipt => _candidate;
    public bool CanEdit => _initialized && State == CheckoutState.Ready && Data.Session.CanSell && Data.Session.ShiftOpen;
    public bool CanUnlockUnpaidOrder => _canReleaseUnpaid && _candidate is not null && State == CheckoutState.NotSaved;
    public string Notice { get; private set; } = "";

    public async Task InitializeAsync(CancellationToken token = default)
    {
        if (_initialized) throw new BusinessException("Phân hệ Cashier đã được khởi tạo; không nạp lặp giao dịch đang chờ.");
        Data = await source.ReadAsync(token);
        var pending = await journal.ReadAsync(token);
        if (pending is not null)
        {
            CartRules.ValidateReceipt(pending);
            _candidate = pending;
            _lines.AddRange(pending.Lines);
            Promotion = pending.Promotion;
            State = CheckoutState.Uncertain;
            Notice = "Có giao dịch chưa xác định kết quả từ lần chạy trước. Chọn Kiểm tra giao dịch trước khi bán tiếp.";
        }
        _initialized = true;
    }

    public async Task RefreshCatalogAsync(CancellationToken token = default)
    {
        if (!_initialized) throw new BusinessException("Chưa tải dữ liệu Cashier.");
        if (State != CheckoutState.Ready)
            throw new BusinessException("Chỉ tải lại thực đơn khi giỏ sẵn sàng; cần xử lý giao dịch đang chờ trước.");
        // Reload the current session even if the cached session is closed/revoked.
        // A Manager may have reopened the shift or restored permission since that read.
        Data = await source.ReadAsync(token);
        Notice = Data.Session.CanSell && Data.Session.ShiftOpen
            ? "Đã tải lại thực đơn. Giá trong giỏ được kiểm tra trước khi thanh toán."
            : "Đã tải lại phiên. Chưa có quyền bán hoặc ca chưa mở; kiểm tra ca/quyền rồi tải lại.";
    }

    public CartLine AddItemToCart(string productId, int quantity, IEnumerable<OptionSelection> selected)
    {
        EnsureEditable();
        CartRules.ValidateQuantity(quantity);
        var item = FindProduct(productId);
        var options = CartRules.ApplyOption(item, selected);
        var line = new CartLine(Guid.NewGuid(), item.Id, item.Name, item.BasePrice, options, quantity);
        var matching = _lines.FindIndex(x => SameVariant(x, line));
        if (matching >= 0)
        {
            var combined = _lines[matching].Quantity + quantity;
            CartRules.ValidateQuantity(combined);
            line = _lines[matching] with { Quantity = combined };
            _lines[matching] = line;
        }
        else _lines.Add(line);
        CartChanged();
        return line;
    }

    public void UpdateCartItem(Guid lineId, int quantity, IEnumerable<OptionSelection> selected)
    {
        EnsureEditable();
        CartRules.ValidateQuantity(quantity);
        var index = FindLineIndex(lineId);
        var old = _lines[index];
        var item = FindProduct(old.ProductId);
        var options = CartRules.ApplyOption(item, selected);
        var updated = new CartLine(old.LineId, item.Id, item.Name, item.BasePrice, options, quantity);
        var matching = _lines.FindIndex(x => x.LineId != old.LineId && SameVariant(x, updated));
        if (matching >= 0)
        {
            var combined = quantity + _lines[matching].Quantity;
            CartRules.ValidateQuantity(combined);
            updated = updated with { Quantity = combined };
        }
        _lines[index] = updated;
        if (matching >= 0) _lines.RemoveAt(matching);
        CartChanged();
    }

    // Compare full price/option snapshots, not just product ID.
    private static bool SameVariant(CartLine a, CartLine b) =>
        a.ProductId == b.ProductId && a.ProductName == b.ProductName &&
        a.BasePrice == b.BasePrice && a.Options.SequenceEqual(b.Options);

    public void ChangeQuantity(Guid lineId, int quantity)
    {
        var line = _lines[FindLineIndex(lineId)];
        UpdateCartItem(lineId, quantity, line.Options.Select(x => new OptionSelection(x.GroupId, x.ChoiceId)));
    }

    public void RemoveCartItem(Guid lineId)
    {
        EnsureEditable();
        _lines.RemoveAt(FindLineIndex(lineId));
        CartChanged();
    }

    public void ApplyPromotion(string code)
    {
        EnsureEditable();
        if (_lines.Count == 0) throw new BusinessException("Hãy thêm món trước khi áp dụng khuyến mãi.");
        var promotion = Data.Promotions.FirstOrDefault(x => x.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new BusinessException("Không tìm thấy mã khuyến mãi. Mã đang áp dụng được giữ nguyên.");
        var valid = CartRules.ApplyPromotion(promotion, Totals.Subtotal, _now(), _lines, Data.Menu);
        Promotion = valid; // Assign only after validation: a rejected new code cannot replace a valid one.
        Notice = $"Đã áp dụng {valid.Code}: giảm {CartRules.Money(valid.Discount)}.";
    }

    public void RemovePromotion()
    {
        EnsureEditable();
        Promotion = null;
        Notice = "Đã bỏ khuyến mãi.";
    }

    public void StartNewOrder(bool discardConfirmed = false)
    {
        if (State is CheckoutState.Saving or CheckoutState.Uncertain or CheckoutState.NotSaved)
            throw new BusinessException("Cần xử lý giao dịch đang chờ trước khi tạo đơn mới.");
        if (State != CheckoutState.Paid && _lines.Count > 0 && !discardConfirmed)
            throw new BusinessException("Cần xác nhận trước khi bỏ giỏ hàng đang có món.");
        _lines.Clear();
        Promotion = null;
        _candidate = null;
        _canReleaseUnpaid = false;
        PaidReceipt = null;
        State = CheckoutState.Ready;
        Notice = "Đơn mới đã sẵn sàng.";
    }

    // Build a NEW unpaid cart from a persisted receipt; never copy payment/promotion or change the old receipt.
    public async Task CreateSimilarOrderAsync(Receipt receipt, bool discardConfirmed = false,
        CancellationToken token = default)
    {
        await _checkoutGate.WaitAsync(token);
        try
        {
            if (!_initialized) throw new BusinessException("Chưa tải dữ liệu Cashier.");
            if (State is not (CheckoutState.Ready or CheckoutState.Paid))
                throw new BusinessException("Cần xử lý giao dịch đang chờ trước khi tạo đơn tương tự.");
            if (State == CheckoutState.Ready && _lines.Count > 0 && !discardConfirmed)
                throw new BusinessException("Cần xác nhận trước khi thay giỏ hàng đang có món.");
            CartRules.ValidateReceipt(receipt);
            var current = await source.ReadAsync(token);
            if (!current.Session.CanSell || !current.Session.ShiftOpen ||
                string.IsNullOrWhiteSpace(current.Session.StaffId) ||
                string.IsNullOrWhiteSpace(current.Session.StaffName) ||
                string.IsNullOrWhiteSpace(current.Session.ShiftId))
                throw new BusinessException("Phiên thu ngân hoặc ca làm việc không hợp lệ. Liên hệ Quản lý.");
            // Validate every line first. One invalid item must not partially replace the user's cart.
            var replacement = new List<CartLine>();
            foreach (var old in receipt.Lines)
            {
                var item = current.Menu.FirstOrDefault(x => x.Id == old.ProductId)
                    ?? throw new BusinessException(old.ProductName + ": món không còn trong thực đơn. Giỏ hiện tại được giữ nguyên.");
                try
                {
                    var options = CartRules.ApplyOption(item,
                        old.Options.Select(x => new OptionSelection(x.GroupId, x.ChoiceId)));
                    var line = new CartLine(Guid.NewGuid(), item.Id, item.Name, item.BasePrice, options, old.Quantity);
                    var matching = replacement.FindIndex(x => SameVariant(x, line));
                    if (matching >= 0)
                    {
                        var combined = replacement[matching].Quantity + line.Quantity;
                        CartRules.ValidateQuantity(combined);
                        replacement[matching] = replacement[matching] with { Quantity = combined };
                    }
                    else replacement.Add(line);
                }
                catch (BusinessException e)
                {
                    throw new BusinessException(old.ProductName + ": " + e.Message +
                        " Giỏ hiện tại được giữ nguyên; chọn lại món thủ công hoặc dùng hóa đơn khác.");
                }
            }
            _ = replacement.Sum(x => x.LineTotal); // Validate arithmetic before replacing any state.
            token.ThrowIfCancellationRequested();
            Data = current;
            _lines.Clear();
            _lines.AddRange(replacement);
            Promotion = null;
            _candidate = null;
            _canReleaseUnpaid = false;
            PaidReceipt = null;
            State = CheckoutState.Ready;
            Notice = "Đã tạo giỏ mới từ " + receipt.ReceiptNumber +
                " theo giá hiện tại. Chưa thanh toán; không sao chép khuyến mãi hoặc tiền nhận cũ.";
        }
        finally { _checkoutGate.Release(); }
    }

    public async Task<CheckoutResult> Checkout(PaymentRequest request, CancellationToken token = default)
    {
        await _checkoutGate.WaitAsync(token);
        try
        {
            if (PaidReceipt is not null) return new(CheckoutResultKind.Paid, "Giao dịch đã ghi nhận trước đó.", PaidReceipt);
            if (_candidate is not null)
            {
                var recovered = await CheckExistingAsync(token);
                if (recovered is not null) return recovered;
                // NOT FOUND permits an idempotent retry of the same frozen identity, NOT a new order.
                return await PersistCandidateAsync(token);
            }
            try
            {
                State = CheckoutState.Saving;
                if (_lines.Count == 0) throw new BusinessException("Giỏ hàng trống.");
                Data = await source.ReadAsync(token);
                if (!Data.Session.CanSell || !Data.Session.ShiftOpen ||
                    string.IsNullOrWhiteSpace(Data.Session.StaffId) || string.IsNullOrWhiteSpace(Data.Session.ShiftId))
                    throw new BusinessException("Phiên thu ngân hoặc ca làm việc không hợp lệ. Liên hệ Quản lý.");
                foreach (var line in _lines)
                {
                    var latest = FindProduct(line.ProductId);
                    var options = CartRules.ApplyOption(latest, line.Options.Select(x => new OptionSelection(x.GroupId, x.ChoiceId)));
                    if (latest.BasePrice != line.BasePrice || !options.SequenceEqual(line.Options))
                        throw new BusinessException($"Giá/tùy chọn của {line.ProductName} đã thay đổi. Sửa món để cập nhật trước khi thanh toán.");
                }
                if (Promotion is not null)
                {
                    try
                    {
                        var latest = Data.Promotions.FirstOrDefault(x => x.Code.Equals(Promotion.Code, StringComparison.OrdinalIgnoreCase))
                            ?? throw new BusinessException("Khuyến mãi đã bị gỡ.");
                        var checkedPromo = CartRules.ApplyPromotion(latest, Totals.Subtotal, _now(), _lines, Data.Menu);
                        if (checkedPromo != Promotion) throw new BusinessException("Khuyến mãi đã thay đổi.");
                    }
                    catch (BusinessException e)
                    {
                        Promotion = null;
                        throw new BusinessException(e.Message + " Đã bỏ mã; kiểm tra lại tổng tiền rồi xác nhận lại.");
                    }
                }
                var payment = CartRules.ValidatePayment(request, Totals.Payable);
                var id = Guid.NewGuid();
                var now = _now();
                var candidate = new Receipt(id, $"HD-{now:yyyyMMdd}-{id:N}".ToUpperInvariant(), now,
                    Data.Session.StaffId, Data.Session.StaffName, Data.Session.ShiftId,
                    _lines.ToImmutableArray(), Promotion, Totals, payment, Data.Session.IsDemo);
                CartRules.ValidateReceipt(candidate);
                // Persist intent BEFORE writing the order; a restart must resolve this same identity.
                await journal.WriteAsync(candidate, token);
                _candidate = candidate;
            }
            catch (Exception e)
            {
                State = CheckoutState.Ready;
                Notice = e.Message;
                return new(CheckoutResultKind.Rejected, e.Message);
            }
            return await PersistCandidateAsync(token);
        }
        finally { _checkoutGate.Release(); }
    }

    public async Task<CheckoutResult> ResolvePendingAsync(CancellationToken token = default)
    {
        await _checkoutGate.WaitAsync(token);
        try
        {
            if (PaidReceipt is not null) return new(CheckoutResultKind.Paid, "Giao dịch đã được lưu.", PaidReceipt);
            if (_candidate is null) return new(CheckoutResultKind.Rejected, "Không có giao dịch cần kiểm tra.");
            var result = await CheckExistingAsync(token);
            if (result is not null) return result;
            State = CheckoutState.NotSaved;
            Notice = "Chưa tìm thấy giao dịch khi kiểm tra. Chỉ thử ghi lại cùng mã/nội dung, không thu thêm tiền.";
            if (!_canReleaseUnpaid) Notice += " Chưa được bỏ giao dịch này hoặc mở giỏ sang một đơn khác.";
            return new(CheckoutResultKind.NotSaved, Notice);
        }
        finally { _checkoutGate.Release(); }
    }

    public async Task UnlockUnpaidOrderAsync(CancellationToken token = default)
    {
        await _checkoutGate.WaitAsync(token);
        try
        {
            if (_candidate is null) return;
            var result = await CheckExistingAsync(token);
            if (result is not null) throw new BusinessException(result.Message);
            if (!_canReleaseUnpaid)
                throw new BusinessException("Kho dữ liệu chưa xác nhận kết quả cuối cùng. Không được mở giỏ hoặc tạo mã đơn mới; chỉ thử lại cùng giao dịch đang chờ.");
            await journal.ClearAsync(token);
            _candidate = null;
            _canReleaseUnpaid = false;
            State = CheckoutState.Ready;
            Notice = "Đã mở lại giỏ chưa được lưu. Nếu đã nhận tiền, thu ngân cần xử lý khoản đã nhận trước khi đổi đơn.";
        }
        finally { _checkoutGate.Release(); }
    }

    private async Task<CheckoutResult> PersistCandidateAsync(CancellationToken token)
    {
        State = CheckoutState.Saving;
        try
        {
            var saved = await orders.SaveAsync(_candidate!, token);
            return await MarkPaidAsync(saved, token);
        }
        catch (Exception e)
        {
            var recovered = await CheckExistingAsync(CancellationToken.None);
            if (recovered is not null) return recovered;
            State = CheckoutState.NotSaved;
            Notice = "Chưa tìm thấy giao dịch sau lỗi ghi: " + e.Message + " Giữ nguyên đơn và tiền đã nhận; chỉ thử lại cùng mã, không thu thêm tiền.";
            if (!_canReleaseUnpaid) Notice += " Không được mở giỏ để tạo đơn khác.";
            return new(CheckoutResultKind.NotSaved, Notice);
        }
    }

    // null means read succeeded and returned NOT FOUND; release requires separate proof.
    private async Task<CheckoutResult?> CheckExistingAsync(CancellationToken token)
    {
        try
        {
            _canReleaseUnpaid = false;
            var existing = await orders.FindAsync(_candidate!.OrderId, token);
            if (existing is not null) return await MarkPaidAsync(existing, token);
            _canReleaseUnpaid = await orders.CanReleaseUnpaidAsync(_candidate.OrderId, token);
            return null;
        }
        catch (Exception e)
        {
            State = CheckoutState.Uncertain;
            Notice = "Chưa xác định kết quả ghi nhận: " + e.Message + " Chọn Kiểm tra giao dịch; không tạo đơn khác hoặc thu lại tiền.";
            return new(CheckoutResultKind.Uncertain, Notice);
        }
    }

    private async Task<CheckoutResult> MarkPaidAsync(Receipt receipt, CancellationToken token)
    {
        CartRules.ValidateReceipt(receipt);
        if (_candidate is null || !ReceiptIdentity.Same(receipt, _candidate))
            throw new BusinessException("Dữ liệu lưu không khớp giao dịch đang chờ; cần kiểm tra, không ghi đè.");
        PaidReceipt = receipt;
        _canReleaseUnpaid = false;
        State = CheckoutState.Paid;
        Notice = "Thanh toán đã được lưu. Có thể xem hoặc in hóa đơn.";
        try { await journal.ClearAsync(token); }
        catch { Notice += " Bản chờ chưa xóa được; lần khởi động sau sẽ kiểm tra lại, không ghi trùng."; }
        return new(CheckoutResultKind.Paid, Notice, receipt);
    }

    public MenuItem FindProduct(string id) => Data.Menu.FirstOrDefault(x => x.Id == id)
        ?? throw new BusinessException("Món không còn trong danh mục.");
    private int FindLineIndex(Guid id)
    {
        var index = _lines.FindIndex(x => x.LineId == id);
        return index >= 0 ? index : throw new BusinessException("Không tìm thấy dòng giỏ hàng.");
    }
    private void EnsureEditable()
    {
        if (!_initialized) throw new BusinessException("Chưa tải dữ liệu Cashier.");
        if (!Data.Session.CanSell || !Data.Session.ShiftOpen)
            throw new BusinessException("Phiên thu ngân hoặc ca làm việc không hợp lệ. Liên hệ Quản lý.");
        if (!CanEdit) throw new BusinessException("Giỏ đã khóa trong khi xử lý thanh toán; không được sửa.");
    }
    private void CartChanged()
    {
        Notice = Promotion is null ? "Đã cập nhật giỏ hàng." : "Giỏ hàng thay đổi: đã bỏ mã khuyến mãi. Áp dụng lại nếu cần.";
        Promotion = null;
    }
}
