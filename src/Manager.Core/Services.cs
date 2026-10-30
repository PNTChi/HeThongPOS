using System.Collections.Immutable;
using Cashier.Core;

namespace Manager.Core;

public sealed class ManagerController(
    ICatalogStore catalogStore,
    IAccountStore accountStore,
    IShiftStore shiftStore,
    ITransactionReader transactionReader,
    Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> _now = clock ?? (() => DateTimeOffset.Now);

    public StaffAccount? CurrentUser { get; private set; }

    public async Task<StaffAccount> LoginAsync(string username, string password, CancellationToken token = default)
    {
        var accounts = await accountStore.GetAccountsAsync(token);
        var staff = ManagerRules.Authenticate(username, password, accounts);
        CurrentUser = staff;
        return staff;
    }

    public void Logout() => CurrentUser = null;

    // --- Shift Operations (UC07, UC08) ---

    public async Task<ShiftRecord?> GetCurrentOpenShiftAsync(CancellationToken token = default) =>
        await shiftStore.GetCurrentOpenShiftAsync(token);

    public async Task<ShiftRecord> OpenShiftAsync(string shiftId, decimal startingCash, CancellationToken token = default)
    {
        if (CurrentUser is null) throw new BusinessException("Chưa đăng nhập.");
        var openShift = await shiftStore.GetCurrentOpenShiftAsync(token);
        ManagerRules.ValidateOpenShift(CurrentUser, startingCash, openShift);

        var trimmedShiftId = string.IsNullOrWhiteSpace(shiftId)
            ? "CA-" + _now().ToString("yyyyMMdd-HHmmss")
            : shiftId.Trim();

        var newShift = new ShiftRecord(
            ShiftId: trimmedShiftId,
            StaffId: CurrentUser.StaffId,
            StaffName: CurrentUser.FullName,
            OpenedAt: _now(),
            ClosedAt: null,
            StartingCash: startingCash,
            ActualCash: null,
            ExpectedCash: startingCash,
            ExpectedTransfer: 0,
            Difference: 0,
            Notes: null,
            Status: ShiftStatus.Open);

        await shiftStore.SaveShiftAsync(newShift, token);
        return newShift;
    }

    public async Task<ShiftReconciliation> PreviewShiftReconciliationAsync(decimal actualCash, string? notes, CancellationToken token = default)
    {
        if (CurrentUser is null) throw new BusinessException("Chưa đăng nhập.");
        var openShift = await shiftStore.GetCurrentOpenShiftAsync(token)
            ?? throw new BusinessException("Hiện không có ca làm việc nào đang mở.");

        var allReceipts = await transactionReader.ReadAllReceiptsAsync(token);
        return ManagerRules.BuildReconciliation(openShift, CurrentUser, actualCash, notes, allReceipts, _now());
    }

    public async Task<ShiftRecord> CloseShiftAsync(decimal actualCash, string? notes, CancellationToken token = default)
    {
        var recon = await PreviewShiftReconciliationAsync(actualCash, notes, token);
        var openShift = (await shiftStore.GetCurrentOpenShiftAsync(token))!;

        var closedShift = openShift with
        {
            ClosedAt = recon.ClosedAt,
            ActualCash = recon.ActualCash,
            ExpectedCash = recon.ExpectedCash,
            ExpectedTransfer = recon.TransferTotal,
            Difference = recon.Difference,
            Notes = notes,
            Status = ShiftStatus.Closed
        };

        await shiftStore.SaveShiftAsync(closedShift, token);
        return closedShift;
    }

    public async Task<ImmutableArray<ShiftRecord>> GetShiftHistoryAsync(CancellationToken token = default) =>
        await shiftStore.GetShiftsAsync(token);

    // --- Product & Category & Modifier Operations (UC09) ---

    public async Task<ImmutableArray<MenuItem>> GetMenuAsync(CancellationToken token = default) =>
        await catalogStore.GetMenuAsync(token);

    public async Task SaveMenuItemAsync(MenuItem item, bool isNew, CancellationToken token = default)
    {
        if (CurrentUser is null || CurrentUser.Role != UserRole.Manager)
            throw new BusinessException("Chỉ Quản lý mới có quyền chỉnh sửa danh mục món.");

        var menu = await catalogStore.GetMenuAsync(token);
        ManagerRules.ValidateMenuItem(item, menu, isNew);

        var list = menu.ToList();
        if (isNew)
        {
            list.Add(item);
        }
        else
        {
            var idx = list.FindIndex(x => x.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) list[idx] = item;
            else list.Add(item);
        }

        await catalogStore.SaveMenuAsync(list.ToImmutableArray(), token);
    }

    public async Task DeleteMenuItemAsync(string itemId, CancellationToken token = default)
    {
        if (CurrentUser is null || CurrentUser.Role != UserRole.Manager)
            throw new BusinessException("Chỉ Quản lý mới có quyền xóa sản phẩm.");

        var menu = await catalogStore.GetMenuAsync(token);
        var itemExists = menu.Any(x => x.Id.Equals(itemId, StringComparison.OrdinalIgnoreCase));
        if (!itemExists)
            throw new BusinessException($"Không tìm thấy sản phẩm có mã '{itemId}'. Danh sách chưa được thay đổi.");

        await catalogStore.DeleteMenuItemAsync(itemId, token);
    }

    public async Task<ImmutableArray<string>> GetCategoriesAsync(CancellationToken token = default) =>
        await catalogStore.GetCategoriesAsync(token);

    public async Task CreateCategoryAsync(string category, CancellationToken token = default)
    {
        EnsureCategoryManager();
        var name = NormalizeCategoryName(category);
        var categories = await catalogStore.GetCategoriesAsync(token);
        if (categories.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new BusinessException($"Danh mục '{name}' đã tồn tại.");

        await catalogStore.CreateCategoryAsync(name, token);
    }

    public async Task RenameCategoryAsync(string oldName, string newName, CancellationToken token = default)
    {
        EnsureCategoryManager();
        var oldCategory = NormalizeCategoryName(oldName);
        var newCategory = NormalizeCategoryName(newName);
        var categories = await catalogStore.GetCategoriesAsync(token);
        if (!categories.Contains(oldCategory, StringComparer.OrdinalIgnoreCase))
            throw new BusinessException($"Không tìm thấy danh mục '{oldCategory}'.");
        if (categories.Any(category => !category.Equals(oldCategory, StringComparison.OrdinalIgnoreCase) &&
                                       category.Equals(newCategory, StringComparison.OrdinalIgnoreCase)))
            throw new BusinessException($"Danh mục '{newCategory}' đã tồn tại.");

        await catalogStore.RenameCategoryAsync(oldCategory, newCategory, token);
    }

    public async Task DeleteCategoryAsync(string category, CancellationToken token = default)
    {
        EnsureCategoryManager();
        var name = NormalizeCategoryName(category);
        var categories = await catalogStore.GetCategoriesAsync(token);
        if (!categories.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new BusinessException($"Không tìm thấy danh mục '{name}'.");

        var menu = await catalogStore.GetMenuAsync(token);
        if (menu.Any(item => item.Category.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new BusinessException("Không thể xóa danh mục còn sản phẩm. Hãy chuyển các sản phẩm sang danh mục khác trước.");

        var promotions = await catalogStore.GetPromotionsAsync(token);
        if (promotions.Any(promo => !promo.Categories.IsDefaultOrEmpty &&
                                    promo.Categories.Contains(name, StringComparer.OrdinalIgnoreCase)))
            throw new BusinessException("Không thể xóa danh mục đang được khuyến mãi sử dụng. Hãy sửa điều kiện khuyến mãi trước.");

        await catalogStore.DeleteCategoryAsync(name, token);
    }

    private void EnsureCategoryManager()
    {
        if (CurrentUser is null || CurrentUser.Role != UserRole.Manager)
            throw new BusinessException("Chỉ Quản lý mới có quyền quản lý danh mục.");
    }

    private static string NormalizeCategoryName(string category)
    {
        var name = category?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            throw new BusinessException("Tên danh mục không được để trống.");
        if (name.Length > 80)
            throw new BusinessException("Tên danh mục không được vượt quá 80 ký tự.");
        return name;
    }

    // --- Promotion Operations (UC10) ---

    public async Task<ImmutableArray<Promotion>> GetPromotionsAsync(CancellationToken token = default) =>
        await catalogStore.GetPromotionsAsync(token);

    public async Task SavePromotionAsync(Promotion promo, bool isNew, CancellationToken token = default)
    {
        if (CurrentUser is null || CurrentUser.Role != UserRole.Manager)
            throw new BusinessException("Chỉ Quản lý mới có quyền cấu hình khuyến mãi.");

        var promos = await catalogStore.GetPromotionsAsync(token);
        var menu = await catalogStore.GetMenuAsync(token);
        ManagerRules.ValidatePromotion(promo, promos, isNew, menu);

        var list = promos.ToList();
        if (isNew)
        {
            list.Add(promo);
        }
        else
        {
            var idx = list.FindIndex(x => x.Code.Equals(promo.Code, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) list[idx] = promo;
            else list.Add(promo);
        }

        await catalogStore.SavePromotionsAsync(list.ToImmutableArray(), token);
    }

    public async Task DeletePromotionAsync(string promoCode, CancellationToken token = default)
    {
        if (CurrentUser is null || CurrentUser.Role != UserRole.Manager)
            throw new BusinessException("Chỉ Quản lý mới có quyền xóa khuyến mãi.");

        var promos = await catalogStore.GetPromotionsAsync(token);
        var filtered = promos.Where(x => !x.Code.Equals(promoCode, StringComparison.OrdinalIgnoreCase)).ToImmutableArray();
        await catalogStore.SavePromotionsAsync(filtered, token);
    }

    // --- Account / Staff Operations (UC11) ---

    public async Task<ImmutableArray<StaffAccount>> GetAccountsAsync(CancellationToken token = default) =>
        await accountStore.GetAccountsAsync(token);

    public async Task SaveAccountAsync(StaffAccount account, bool isNew, CancellationToken token = default)
    {
        if (CurrentUser is null || CurrentUser.Role != UserRole.Manager)
            throw new BusinessException("Chỉ Quản lý mới có quyền quản lý tài khoản nhân viên.");

        var accounts = await accountStore.GetAccountsAsync(token);
        ManagerRules.ValidateStaffAccount(account, accounts, isNew);

        var list = accounts.ToList();
        if (isNew)
        {
            list.Add(account);
        }
        else
        {
            var idx = list.FindIndex(x => x.StaffId.Equals(account.StaffId, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) list[idx] = account;
            else list.Add(account);
        }

        await accountStore.SaveAccountsAsync(list.ToImmutableArray(), token);
    }

    // --- History Query (UC12) ---

    public async Task<ImmutableArray<Receipt>> QueryTransactionsAsync(HistoryFilter filter, CancellationToken token = default)
    {
        var receipts = await transactionReader.ReadAllReceiptsAsync(token);
        return ManagerRules.FilterTransactions(receipts, filter).ToImmutableArray();
    }

    // --- Reports & Statistics (UC13) ---

    public async Task<ReportSummary> GenerateReportAsync(
        ReportKind kind,
        DateTimeOffset fromDate,
        DateTimeOffset toDate,
        string? shiftId = null,
        CancellationToken token = default)
    {
        var receipts = await transactionReader.ReadAllReceiptsAsync(token);
        return ManagerRules.GenerateReport(receipts, kind, fromDate, toDate, shiftId);
    }
}
