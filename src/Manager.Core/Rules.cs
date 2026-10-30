using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Cashier.Core;

namespace Manager.Core;

public static class ManagerRules
{
    public static void ValidateStaffAccount(StaffAccount account, IEnumerable<StaffAccount> existing, bool isNew)
    {
        if (string.IsNullOrWhiteSpace(account.StaffId))
            throw new BusinessException("Mã nhân viên không được để trống.");
        if (string.IsNullOrWhiteSpace(account.FullName))
            throw new BusinessException("Họ và tên nhân viên không được để trống.");
        if (string.IsNullOrWhiteSpace(account.Username))
            throw new BusinessException("Tên đăng nhập không được để trống.");
        if (string.IsNullOrWhiteSpace(account.Password))
            throw new BusinessException("Mật khẩu không được để trống.");

        var idTrimmed = account.StaffId.Trim();
        var userTrimmed = account.Username.Trim();

        if (isNew)
        {
            if (existing.Any(x => x.StaffId.Equals(idTrimmed, StringComparison.OrdinalIgnoreCase)))
                throw new BusinessException($"Mã nhân viên '{account.StaffId}' đã tồn tại.");
            if (existing.Any(x => x.Username.Equals(userTrimmed, StringComparison.OrdinalIgnoreCase)))
                throw new BusinessException($"Tên đăng nhập '{account.Username}' đã tồn tại.");
        }
        else
        {
            if (existing.Any(x => !x.StaffId.Equals(idTrimmed, StringComparison.OrdinalIgnoreCase) &&
                                  x.Username.Equals(userTrimmed, StringComparison.OrdinalIgnoreCase)))
                throw new BusinessException($"Tên đăng nhập '{account.Username}' đã được sử dụng bởi nhân viên khác.");
        }
    }

    public static StaffAccount Authenticate(string username, string password, IEnumerable<StaffAccount> accounts)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new BusinessException("Vui lòng nhập tên đăng nhập.");
        if (string.IsNullOrWhiteSpace(password))
            throw new BusinessException("Vui lòng nhập mật khẩu.");

        var account = accounts.FirstOrDefault(x =>
            x.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase) &&
            x.Password == password);

        if (account is null)
            throw new BusinessException("Tài khoản hoặc mật khẩu không chính xác.");

        if (!account.IsActive)
            throw new BusinessException("Tài khoản này đã bị khóa hoặc ngừng hoạt động.");

        return account;
    }

    public static void ValidateOpenShift(StaffAccount staff, decimal startingCash, ShiftRecord? currentOpenShift)
    {
        if (currentOpenShift is not null && currentOpenShift.Status == ShiftStatus.Open)
            throw new BusinessException($"Đang có ca làm việc '{currentOpenShift.ShiftId}' chưa kết thúc. Vui lòng đóng ca cũ trước khi mở ca mới.");

        if (!staff.CanManageShift)
            throw new BusinessException("Tài khoản không có quyền mở/thao tác ca làm việc.");

        if (startingCash < 0 || decimal.Floor(startingCash) != startingCash)
            throw new BusinessException("Số tiền đầu ca không hợp lệ (phải là số nguyên không âm).");
    }

    public static ShiftReconciliation BuildReconciliation(
        ShiftRecord openShift,
        StaffAccount staff,
        decimal actualCash,
        string? notes,
        IEnumerable<Receipt> shiftReceipts,
        DateTimeOffset closedAt)
    {
        if (openShift.Status != ShiftStatus.Open)
            throw new BusinessException("Ca này đã được đóng trước đó.");

        if (!staff.CanManageShift)
            throw new BusinessException("Tài khoản không có quyền đóng ca và đối soát.");

        if (actualCash < 0 || decimal.Floor(actualCash) != actualCash)
            throw new BusinessException("Số tiền thực tế phải là số nguyên không âm.");

        var receipts = shiftReceipts
            .Where(r => r.ShiftId.Equals(openShift.ShiftId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var totalOrders = receipts.Count;
        var totalRevenue = receipts.Sum(r => r.Totals.Payable);

        var cashOrders = receipts.Where(r => r.Payment.Method == PaymentMethod.Cash).ToList();
        var cashTotal = cashOrders.Sum(r => r.Totals.Payable);

        var transferOrders = receipts.Where(r => r.Payment.Method == PaymentMethod.BankTransfer).ToList();
        var transferTotal = transferOrders.Sum(r => r.Totals.Payable);

        var expectedCash = openShift.StartingCash + cashTotal;
        var diff = actualCash - expectedCash;

        return new ShiftReconciliation(
            ShiftId: openShift.ShiftId,
            StaffId: openShift.StaffId,
            StaffName: openShift.StaffName,
            OpenedAt: openShift.OpenedAt,
            ClosedAt: closedAt,
            TotalOrders: totalOrders,
            TotalRevenue: totalRevenue,
            CashOrdersCount: cashOrders.Count,
            CashTotal: cashTotal,
            TransferOrdersCount: transferOrders.Count,
            TransferTotal: transferTotal,
            StartingCash: openShift.StartingCash,
            ActualCash: actualCash,
            ExpectedCash: expectedCash,
            Difference: diff,
            Notes: notes);
    }

    public static void ValidateMenuItem(MenuItem item, IEnumerable<MenuItem> existingItems, bool isNew)
    {
        if (string.IsNullOrWhiteSpace(item.Id) ||
            !Regex.IsMatch(item.Id.Trim(), "^MON-(?:0[1-9]|[1-9][0-9]{1,})$", RegexOptions.CultureInvariant))
            throw new BusinessException("Mã món phải theo định dạng MON-01, MON-02, ...");
        if (string.IsNullOrWhiteSpace(item.Name))
            throw new BusinessException("Tên món không được để trống.");
        if (string.IsNullOrWhiteSpace(item.Category))
            throw new BusinessException("Danh mục món không được để trống.");
        if (item.BasePrice < 0 || decimal.Floor(item.BasePrice) != item.BasePrice)
            throw new BusinessException("Đơn giá món phải là số nguyên không âm (VND).");

        var idTrimmed = item.Id.Trim();
        if (isNew && existingItems.Any(x => x.Id.Equals(idTrimmed, StringComparison.OrdinalIgnoreCase)))
            throw new BusinessException($"Mã món '{item.Id}' đã tồn tại.");

        // Check options
        if (!item.OptionGroups.IsDefaultOrEmpty)
        {
            var groupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in item.OptionGroups)
            {
                ValidateOptionGroup(group);
                if (!groupIds.Add(group.Id))
                    throw new BusinessException($"Nhóm tùy chọn '{group.Id}' bị trùng trong cùng sản phẩm.");
            }
        }
    }

    public static string NextMenuItemId(IEnumerable<MenuItem> existingItems)
    {
        var highest = existingItems
            .Select(item => item.Id.Trim())
            .Where(id => id.StartsWith("MON-", StringComparison.OrdinalIgnoreCase))
            .Select(id => int.TryParse(id.AsSpan(4), out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"MON-{highest + 1:D2}";
    }

    public static void ValidateOptionGroup(OptionGroup group)
    {
        if (string.IsNullOrWhiteSpace(group.Id))
            throw new BusinessException("Mã nhóm tùy chọn không được để trống.");
        if (string.IsNullOrWhiteSpace(group.Name))
            throw new BusinessException("Tên nhóm tùy chọn không được để trống.");
        if (group.Minimum < 0)
            throw new BusinessException("Số lượng chọn tối thiểu không được âm.");
        if (group.Maximum < group.Minimum)
            throw new BusinessException("Số lượng chọn tối đa phải lớn hơn hoặc bằng tối thiểu.");
        if (group.Choices.IsDefaultOrEmpty)
            throw new BusinessException($"Nhóm tùy chọn '{group.Name}' phải có ít nhất một lựa chọn.");

        var choiceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var choice in group.Choices)
        {
            ValidateOptionChoice(choice);
            if (!choiceIds.Add(choice.Id))
                throw new BusinessException($"Mã lựa chọn '{choice.Id}' bị trùng trong nhóm '{group.Name}'.");
        }
    }

    public static void ValidateOptionChoice(OptionChoice choice)
    {
        if (string.IsNullOrWhiteSpace(choice.Id))
            throw new BusinessException("Mã tùy chọn không được để trống.");
        if (string.IsNullOrWhiteSpace(choice.Name))
            throw new BusinessException("Tên tùy chọn không được để trống.");
        if (choice.ExtraPrice < 0 || decimal.Floor(choice.ExtraPrice) != choice.ExtraPrice)
            throw new BusinessException("Phụ thu tùy chọn phải là số nguyên không âm (VND).");
    }

    public static void ValidatePromotion(Promotion promo, IEnumerable<Promotion> existingPromos, bool isNew,
        IEnumerable<MenuItem>? menu = null)
    {
        if (string.IsNullOrWhiteSpace(promo.Code))
            throw new BusinessException("Mã khuyến mãi không được để trống.");
        if (string.IsNullOrWhiteSpace(promo.Description))
            throw new BusinessException("Mô tả chương trình khuyến mãi không được để trống.");
        if (promo.Value <= 0)
            throw new BusinessException("Mức giảm giá phải lớn hơn 0.");

        if (promo.Kind == DiscountKind.Percentage)
        {
            if (promo.Value > 100)
                throw new BusinessException("Mức giảm theo phần trăm không được vượt quá 100%.");
        }
        else
        {
            if (decimal.Floor(promo.Value) != promo.Value)
                throw new BusinessException("Mức giảm cố định phải là số nguyên đồng (VND).");
        }

        if (promo.MinimumSubtotal < 0 || decimal.Floor(promo.MinimumSubtotal) != promo.MinimumSubtotal)
            throw new BusinessException("Đơn hàng tối thiểu phải là số nguyên không âm.");

        var productIds = promo.ProductIds.IsDefault ? [] : promo.ProductIds;
        var categories = promo.Categories.IsDefault ? [] : promo.Categories;
        if (productIds.Any(string.IsNullOrWhiteSpace) || categories.Any(string.IsNullOrWhiteSpace))
            throw new BusinessException("Điều kiện sản phẩm/danh mục không hợp lệ.");
        if (productIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != productIds.Length ||
            categories.Distinct(StringComparer.OrdinalIgnoreCase).Count() != categories.Length)
            throw new BusinessException("Không được lặp sản phẩm hoặc danh mục trong điều kiện áp dụng.");
        if (productIds.Length > 0 || categories.Length > 0)
        {
            var catalog = menu?.ToArray() ?? [];
            var availableProductIds = catalog.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var availableCategories = catalog.Select(x => x.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (productIds.Any(id => !availableProductIds.Contains(id)))
                throw new BusinessException("Điều kiện đang chứa sản phẩm không còn trong thực đơn.");
            if (categories.Any(category => !availableCategories.Contains(category)))
                throw new BusinessException("Điều kiện đang chứa danh mục không còn trong thực đơn.");
        }

        if (promo.StartsAt > promo.EndsAt)
            throw new BusinessException("Thời điểm bắt đầu không được lớn hơn thời điểm kết thúc.");

        var codeTrimmed = promo.Code.Trim();
        if (isNew && existingPromos.Any(x => x.Code.Equals(codeTrimmed, StringComparison.OrdinalIgnoreCase)))
            throw new BusinessException($"Mã khuyến mãi '{promo.Code}' đã tồn tại.");
    }

    public static IEnumerable<Receipt> FilterTransactions(IEnumerable<Receipt> receipts, HistoryFilter filter)
    {
        var query = receipts.AsEnumerable();

        if (filter.FromDate.HasValue)
            query = query.Where(r => r.PaidAt >= filter.FromDate.Value);

        if (filter.ToDate.HasValue)
            query = query.Where(r => r.PaidAt <= filter.ToDate.Value);

        if (!string.IsNullOrWhiteSpace(filter.StaffQuery))
        {
            var s = filter.StaffQuery.Trim();
            query = query.Where(r =>
                r.StaffName.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                r.StaffId.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                r.ShiftId.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter.ShiftQuery))
        {
            var shift = filter.ShiftQuery.Trim();
            query = query.Where(r => r.ShiftId.Contains(shift, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.Method.HasValue)
            query = query.Where(r => r.Payment.Method == filter.Method.Value);

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var kw = filter.Keyword.Trim();
            query = query.Where(r =>
                r.ReceiptNumber.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                r.OrderId.ToString("N").Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                r.Lines.Any(l => l.ProductName.Contains(kw, StringComparison.OrdinalIgnoreCase)));
        }

        return query.OrderByDescending(r => r.PaidAt);
    }

    public static ReportSummary GenerateReport(
        IEnumerable<Receipt> receipts,
        ReportKind kind,
        DateTimeOffset fromDate,
        DateTimeOffset toDate,
        string? specificShiftId = null)
    {
        var matched = receipts
            .Where(r => r.PaidAt >= fromDate && r.PaidAt <= toDate);

        if (!string.IsNullOrWhiteSpace(specificShiftId))
            matched = matched.Where(r => r.ShiftId.Equals(specificShiftId.Trim(), StringComparison.OrdinalIgnoreCase));

        var list = matched.ToList();

        var totalOrders = list.Count;
        var totalRevenue = list.Sum(r => r.Totals.Payable);
        var totalDiscount = list.Sum(r => r.Totals.Discount);

        var cashOrders = list.Where(r => r.Payment.Method == PaymentMethod.Cash).ToList();
        var cashTotal = cashOrders.Sum(r => r.Totals.Payable);

        var transferOrders = list.Where(r => r.Payment.Method == PaymentMethod.BankTransfer).ToList();
        var transferTotal = transferOrders.Sum(r => r.Totals.Payable);

        var metrics = ImmutableArray.CreateBuilder<ReportMetric>();
        var topProducts = ImmutableArray.CreateBuilder<ProductSalesStat>();

        // Build top products
        var productGroups = list
            .SelectMany(r => r.Lines)
            .GroupBy(l => l.ProductId)
            .Select(g =>
            {
                var first = g.First();
                return new ProductSalesStat(
                    ProductId: g.Key,
                    ProductName: first.ProductName,
                    Category: "Sản phẩm",
                    QuantitySold: g.Sum(x => x.Quantity),
                    Revenue: g.Sum(x => x.LineTotal));
            })
            .OrderByDescending(x => x.QuantitySold)
            .ThenByDescending(x => x.Revenue);

        topProducts.AddRange(productGroups);

        switch (kind)
        {
            case ReportKind.RevenueByPeriod:
                var byDate = list.GroupBy(r => r.PaidAt.LocalDateTime.Date)
                    .OrderBy(g => g.Key);
                foreach (var g in byDate)
                {
                    metrics.Add(new ReportMetric(
                        Indicator: $"Ngày {g.Key:dd/MM/yyyy}",
                        Quantity: g.Count(),
                        Value: g.Sum(x => x.Totals.Payable),
                        Period: g.Key.ToString("dd/MM/yyyy")));
                }
                break;

            case ReportKind.RevenueByShift:
                var byShift = list.GroupBy(r => r.ShiftId)
                    .OrderBy(g => g.Key);
                foreach (var g in byShift)
                {
                    metrics.Add(new ReportMetric(
                        Indicator: $"Ca {g.Key}",
                        Quantity: g.Count(),
                        Value: g.Sum(x => x.Totals.Payable),
                        Period: $"Thu ngân: {g.First().StaffName}"));
                }
                break;

            case ReportKind.TopProducts:
                foreach (var p in topProducts)
                {
                    metrics.Add(new ReportMetric(
                        Indicator: p.ProductName,
                        Quantity: p.QuantitySold,
                        Value: p.Revenue,
                        Period: $"{p.QuantitySold} món đã bán"));
                }
                break;

            case ReportKind.PaymentBreakdown:
                metrics.Add(new ReportMetric(
                    Indicator: "Tiền mặt (Cash)",
                    Quantity: cashOrders.Count,
                    Value: cashTotal,
                    Period: totalRevenue > 0 ? $"{cashTotal / totalRevenue * 100:0.1}%" : "0%"));
                metrics.Add(new ReportMetric(
                    Indicator: "Chuyển khoản (Bank Transfer)",
                    Quantity: transferOrders.Count,
                    Value: transferTotal,
                    Period: totalRevenue > 0 ? $"{transferTotal / totalRevenue * 100:0.1}%" : "0%"));
                break;
        }

        return new ReportSummary(
            Kind: kind,
            FromDate: fromDate,
            ToDate: toDate,
            TotalOrders: totalOrders,
            TotalRevenue: totalRevenue,
            TotalDiscount: totalDiscount,
            CashTotal: cashTotal,
            TransferTotal: transferTotal,
            Metrics: metrics.ToImmutable(),
            TopProducts: topProducts.ToImmutable());
    }
}

