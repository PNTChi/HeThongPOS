using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Cashier.Core;

public static class CartRules
{
    public const int MaximumQuantity = 999;
    public static string Money(decimal amount) => amount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")) + " đ";

    public static string SearchKey(string value)
    {
        var output = new StringBuilder();
        foreach (var c in value.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                output.Append(c is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(c));
        return output.ToString().Normalize(NormalizationForm.FormC);
    }

    public static void ValidateQuantity(int quantity)
    {
        if (quantity < 1 || quantity > MaximumQuantity)
            throw new BusinessException($"Số lượng phải là số nguyên từ 1 đến {MaximumQuantity}.");
    }

    // Assigned function: validate selections and calculate an option/price snapshot.
    public static ImmutableArray<SelectedOption> ApplyOption(MenuItem item, IEnumerable<OptionSelection> selections)
    {
        if (!item.Available) throw new BusinessException("Món đang tạm ngừng bán.");
        if (item.BasePrice < 0 || decimal.Truncate(item.BasePrice) != item.BasePrice)
            throw new BusinessException("Giá món phải là số tiền VND nguyên, không âm.");
        var selected = selections.ToArray();
        if (selected.Distinct().Count() != selected.Length)
            throw new BusinessException("Một tùy chọn không được chọn lặp lại.");
        if (item.OptionGroups.Select(g => g.Id).Distinct().Count() != item.OptionGroups.Length)
            throw new BusinessException("Cấu hình nhóm tùy chọn bị trùng mã; cần kiểm tra danh mục.");
        var result = ImmutableArray.CreateBuilder<SelectedOption>();
        foreach (var group in item.OptionGroups)
        {
            if (group.Choices.IsDefault || group.Choices.Select(x => x.Id).Distinct().Count() != group.Choices.Length)
                throw new BusinessException($"Cấu hình lựa chọn trong nhóm {group.Name} bị thiếu hoặc trùng mã.");
            if (group.Minimum < 0 || group.Maximum < group.Minimum || group.Maximum < 1)
                throw new BusinessException($"Cấu hình nhóm {group.Name} không hợp lệ.");
            var choices = selected.Where(x => x.GroupId == group.Id).ToArray();
            if (choices.Length < group.Minimum || choices.Length > group.Maximum)
                throw new BusinessException($"{group.Name}: chọn từ {group.Minimum} đến {group.Maximum} tùy chọn.");
            foreach (var choice in choices)
            {
                var definition = group.Choices.SingleOrDefault(x => x.Id == choice.ChoiceId);
                if (definition is null || !definition.Available)
                    throw new BusinessException($"Tùy chọn trong nhóm {group.Name} không còn được bán.");
                if (definition.ExtraPrice < 0 || decimal.Truncate(definition.ExtraPrice) != definition.ExtraPrice)
                    throw new BusinessException("Phụ thu phải là số tiền VND nguyên, không âm.");
                result.Add(new(group.Id, group.Name, definition.Id, definition.Name, definition.ExtraPrice));
            }
        }
        if (selected.Any(x => !item.OptionGroups.Any(g => g.Id == x.GroupId)))
            throw new BusinessException("Tùy chọn không thuộc món đã chọn.");
        return result.OrderBy(x => x.GroupId, StringComparer.Ordinal)
            .ThenBy(x => x.ChoiceId, StringComparer.Ordinal).ToImmutableArray();
    }

    public static AppliedPromotion ApplyPromotion(Promotion promotion, decimal subtotal, DateTimeOffset now,
        IEnumerable<CartLine>? lines = null, IEnumerable<MenuItem>? menu = null)
    {
        if (!promotion.Active || now < promotion.StartsAt || now > promotion.EndsAt)
            throw new BusinessException("Khuyến mãi chưa có hiệu lực hoặc đã hết hạn.");
        if (subtotal <= 0) throw new BusinessException("Giỏ hàng chưa có giá trị để áp dụng khuyến mãi.");
        if (subtotal < promotion.MinimumSubtotal)
            throw new BusinessException($"Đơn hàng cần từ {Money(promotion.MinimumSubtotal)} để dùng mã này.");

        var productIds = promotion.ProductIds.IsDefault
            ? ImmutableHashSet<string>.Empty
            : promotion.ProductIds.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        var categories = promotion.Categories.IsDefault
            ? ImmutableHashSet<string>.Empty
            : promotion.Categories.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        var discountableSubtotal = subtotal;
        if (productIds.Count > 0 || categories.Count > 0)
        {
            if (lines is null || menu is null)
                throw new BusinessException("Không thể kiểm tra phạm vi sản phẩm của khuyến mãi.");

            var categoryByProductId = menu.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Category,
                    StringComparer.OrdinalIgnoreCase);
            var eligibleLines = lines.Where(line =>
                productIds.Contains(line.ProductId) ||
                (categories.Count > 0 && categoryByProductId.TryGetValue(line.ProductId, out var category) &&
                 categories.Contains(category)));
            discountableSubtotal = eligibleLines.Sum(line => line.LineTotal);
            if (discountableSubtotal <= 0)
                throw new BusinessException("Giỏ hàng không có sản phẩm thuộc phạm vi áp dụng của mã này.");
        }

        if (promotion.Value <= 0 || promotion.MinimumSubtotal < 0 ||
            decimal.Truncate(promotion.MinimumSubtotal) != promotion.MinimumSubtotal || !Enum.IsDefined(promotion.Kind) ||
            (promotion.Kind == DiscountKind.Percentage && promotion.Value > 100) ||
            (promotion.Kind == DiscountKind.FixedAmount && decimal.Truncate(promotion.Value) != promotion.Value))
            throw new BusinessException("Cấu hình khuyến mãi không hợp lệ.");
        var raw = promotion.Kind == DiscountKind.Percentage
            ? discountableSubtotal * promotion.Value / 100
            : promotion.Value;
        var discount = Math.Min(discountableSubtotal, decimal.Round(raw, 0, MidpointRounding.AwayFromZero));
        return new(promotion.Code, promotion.Description, promotion.Kind, promotion.Value, discount,
            discountableSubtotal);
    }

    public static PaymentSnapshot ValidatePayment(PaymentRequest request, decimal payable)
    {
        ValidateVnd(payable);
        if (request.Method == PaymentMethod.Cash)
        {
            if (request.CashReceived < payable)
                throw new BusinessException("Tiền khách đưa chưa đủ để thanh toán.");
            if (request.CashReceived < 0 || decimal.Truncate(request.CashReceived) != request.CashReceived)
                throw new BusinessException("Tiền khách đưa phải là số tiền VND nguyên, không âm.");
            return new(PaymentMethod.Cash, request.CashReceived, request.CashReceived - payable);
        }
        if (request.Method != PaymentMethod.BankTransfer)
            throw new BusinessException("Phương thức thanh toán không được hỗ trợ.");
        if (!request.TransferReceivedConfirmed)
            throw new BusinessException("Cần xác nhận đã nhận tiền chuyển khoản. Chương trình không kiểm tra ngân hàng tự động.");
        return new(PaymentMethod.BankTransfer, payable, 0);
    }

    public static void ValidateReceipt(Receipt receipt)
    {
        if (receipt.OrderId == Guid.Empty || receipt.Lines.IsDefaultOrEmpty ||
            string.IsNullOrWhiteSpace(receipt.ReceiptNumber) || string.IsNullOrWhiteSpace(receipt.StaffId) ||
            string.IsNullOrWhiteSpace(receipt.StaffName) || string.IsNullOrWhiteSpace(receipt.ShiftId) ||
            receipt.Totals is null || receipt.Payment is null)
            throw new BusinessException("Dữ liệu hóa đơn không đầy đủ.");
        if (receipt.Lines.Select(x => x.LineId).Distinct().Count() != receipt.Lines.Length)
            throw new BusinessException("Hóa đơn có mã dòng món bị trùng.");
        foreach (var line in receipt.Lines)
        {
            ValidateQuantity(line.Quantity);
            if (line.LineId == Guid.Empty || string.IsNullOrWhiteSpace(line.ProductId) ||
                string.IsNullOrWhiteSpace(line.ProductName) || line.Options.IsDefault)
                throw new BusinessException("Thông tin dòng món trên hóa đơn không đầy đủ.");
            ValidateVnd(line.BasePrice);
            if (line.Options.Select(x => new OptionSelection(x.GroupId, x.ChoiceId)).Distinct().Count() != line.Options.Length)
                throw new BusinessException("Tùy chọn trên hóa đơn bị lặp.");
            foreach (var option in line.Options)
            {
                if (string.IsNullOrWhiteSpace(option.GroupId) || string.IsNullOrWhiteSpace(option.ChoiceId) ||
                    string.IsNullOrWhiteSpace(option.GroupName) || string.IsNullOrWhiteSpace(option.ChoiceName))
                    throw new BusinessException("Thông tin tùy chọn trên hóa đơn không đầy đủ.");
                ValidateVnd(option.ExtraPrice);
            }
        }
        var subtotal = receipt.Lines.Sum(x => x.LineTotal);
        var discount = receipt.Promotion?.Discount ?? 0;
        ValidateVnd(receipt.Totals.Subtotal);
        ValidateVnd(receipt.Totals.Discount);
        ValidateVnd(receipt.Totals.Payable);
        if (receipt.Promotion is { } promotion)
        {
            if (string.IsNullOrWhiteSpace(promotion.Code) || promotion.Value <= 0 || !Enum.IsDefined(promotion.Kind) ||
                (promotion.Kind == DiscountKind.Percentage && promotion.Value > 100) ||
                (promotion.Kind == DiscountKind.FixedAmount && decimal.Truncate(promotion.Value) != promotion.Value))
                throw new BusinessException("Thông tin khuyến mãi trên hóa đơn không hợp lệ.");
            var discountableSubtotal = promotion.DiscountableSubtotal ?? subtotal;
            if (discountableSubtotal < 0 || discountableSubtotal > subtotal)
                throw new BusinessException("Giá trị được áp dụng khuyến mãi không hợp lệ.");
            var raw = promotion.Kind == DiscountKind.Percentage
                ? discountableSubtotal * promotion.Value / 100
                : promotion.Value;
            if (promotion.Discount != Math.Min(discountableSubtotal,
                    decimal.Round(raw, 0, MidpointRounding.AwayFromZero)))
                throw new BusinessException("Số giảm giá không khớp chính sách đã lưu trên hóa đơn.");
        }
        if (discount < 0 || discount > subtotal || receipt.Totals != new CartTotals(subtotal, discount, subtotal - discount))
            throw new BusinessException("Tổng tiền trên hóa đơn không khớp các dòng món.");
        var payment = ValidatePayment(new(receipt.Payment.Method, receipt.Payment.AmountReceived,
            receipt.Payment.Method == PaymentMethod.BankTransfer), receipt.Totals.Payable);
        if (payment != receipt.Payment) throw new BusinessException("Tiền nhận hoặc tiền thừa trên hóa đơn không khớp.");
    }

    private static void ValidateVnd(decimal value)
    {
        if (value < 0 || decimal.Truncate(value) != value)
            throw new BusinessException("Số tiền VND phải là số nguyên, không âm.");
    }
}
