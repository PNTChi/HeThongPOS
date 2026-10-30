using System.Collections.Immutable;
using Cashier.Core;
using Cashier.Infrastructure;
using Manager.Core;

namespace Manager.Infrastructure;

public sealed class JsonCatalogStore(string catalogPath) : ICatalogStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string CategoriesPath => Path.ChangeExtension(catalogPath, ".categories.json");

    public async Task<ImmutableArray<string>> GetCategoriesAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var data = await ManagerJson.ReadAsync<CatalogData>(catalogPath, token);
            var saved = await ManagerJson.ReadAsync<List<string>>(CategoriesPath, token) ?? [];
            var categories = (data?.Menu ?? []).Select(item => item.Category)
                .Concat(saved)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToImmutableArray();
            if (!categories.SequenceEqual(saved, StringComparer.OrdinalIgnoreCase))
                await ManagerJson.WriteAtomicAsync(CategoriesPath, categories.ToList(), overwrite: true, token);
            return categories;
        }
        finally { _gate.Release(); }
    }

    public async Task CreateCategoryAsync(string category, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var saved = await ManagerJson.ReadAsync<List<string>>(CategoriesPath, token) ?? [];
            if (!saved.Contains(category, StringComparer.OrdinalIgnoreCase)) saved.Add(category);
            await ManagerJson.WriteAtomicAsync(CategoriesPath, saved, overwrite: true, token);
        }
        finally { _gate.Release(); }
    }

    public async Task RenameCategoryAsync(string oldName, string newName, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var existing = await ManagerJson.ReadAsync<CatalogData>(catalogPath, token);
            var menu = (existing?.Menu ?? []).Select(item => item.Category.Equals(oldName, StringComparison.OrdinalIgnoreCase)
                ? item with { Category = newName } : item).ToImmutableArray();
            var promotions = (existing?.Promotions ?? []).Select(promo => promo with
            {
                Categories = (promo.Categories.IsDefault ? ImmutableArray<string>.Empty : promo.Categories)
                    .Select(name => name.Equals(oldName, StringComparison.OrdinalIgnoreCase) ? newName : name)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray()
            }).ToImmutableArray();
            await ManagerJson.WriteAtomicAsync(catalogPath, new CatalogData(menu, promotions), overwrite: true, token);
            var saved = await ManagerJson.ReadAsync<List<string>>(CategoriesPath, token) ?? [];
            var renamed = saved.Select(name => name.Equals(oldName, StringComparison.OrdinalIgnoreCase) ? newName : name)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            await ManagerJson.WriteAtomicAsync(CategoriesPath, renamed, overwrite: true, token);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteCategoryAsync(string category, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var saved = await ManagerJson.ReadAsync<List<string>>(CategoriesPath, token) ?? [];
            saved.RemoveAll(name => name.Equals(category, StringComparison.OrdinalIgnoreCase));
            await ManagerJson.WriteAtomicAsync(CategoriesPath, saved, overwrite: true, token);
        }
        finally { _gate.Release(); }
    }

    public async Task<ImmutableArray<MenuItem>> GetMenuAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var data = await ManagerJson.ReadAsync<CatalogData>(catalogPath, token);
            return data?.Menu ?? [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveMenuAsync(ImmutableArray<MenuItem> menu, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var existing = await ManagerJson.ReadAsync<CatalogData>(catalogPath, token);
            var promotions = existing?.Promotions ?? [];
            var updated = new CatalogData(menu, promotions);
            await ManagerJson.WriteAtomicAsync(catalogPath, updated, overwrite: true, token);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteMenuItemAsync(string itemId, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var existing = await ManagerJson.ReadAsync<CatalogData>(catalogPath, token);
            var promotions = existing?.Promotions ?? [];
            var menu = existing?.Menu ?? [];
            var updated = new CatalogData(
                menu.Where(item => !item.Id.Equals(itemId, StringComparison.OrdinalIgnoreCase)).ToImmutableArray(),
                promotions);
            await ManagerJson.WriteAtomicAsync(catalogPath, updated, overwrite: true, token);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ImmutableArray<Promotion>> GetPromotionsAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var data = await ManagerJson.ReadAsync<CatalogData>(catalogPath, token);
            return data?.Promotions ?? [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SavePromotionsAsync(ImmutableArray<Promotion> promotions, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var existing = await ManagerJson.ReadAsync<CatalogData>(catalogPath, token);
            var menu = existing?.Menu ?? [];
            var updated = new CatalogData(menu, promotions);
            await ManagerJson.WriteAtomicAsync(catalogPath, updated, overwrite: true, token);
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed class JsonAccountStore(string accountsPath) : IAccountStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly ImmutableArray<StaffAccount> DefaultAccounts =
    [
        new("QL01", "Nguyễn Ngọc Yến Nhi", "admin", "123456", UserRole.Manager, IsActive: true, CanSell: true, CanManageShift: true),
        new("TN01", "Nguyễn Tuấn Khôi", "khoi", "123456", UserRole.Cashier, IsActive: true, CanSell: true, CanManageShift: true),
        new("TN02", "Phạm Ngọc Thảo Chi", "chi", "123456", UserRole.Cashier, IsActive: true, CanSell: true, CanManageShift: false)
    ];

    public async Task<ImmutableArray<StaffAccount>> GetAccountsAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var list = await ManagerJson.ReadAsync<List<StaffAccount>>(accountsPath, token);
            if (list is null || list.Count == 0)
            {
                await ManagerJson.WriteAtomicAsync(accountsPath, DefaultAccounts.ToList(), overwrite: true, token);
                return DefaultAccounts;
            }
            return list.ToImmutableArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAccountsAsync(ImmutableArray<StaffAccount> accounts, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            await ManagerJson.WriteAtomicAsync(accountsPath, accounts.ToList(), overwrite: true, token);
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed class JsonShiftStore(string shiftsPath) : IShiftStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ImmutableArray<ShiftRecord>> GetShiftsAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var list = await ManagerJson.ReadAsync<List<ShiftRecord>>(shiftsPath, token);
            return list?.ToImmutableArray() ?? [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ShiftRecord?> GetCurrentOpenShiftAsync(CancellationToken token = default)
    {
        var shifts = await GetShiftsAsync(token);
        return shifts.FirstOrDefault(s => s.Status == ShiftStatus.Open);
    }

    public async Task SaveShiftAsync(ShiftRecord shift, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var list = (await ManagerJson.ReadAsync<List<ShiftRecord>>(shiftsPath, token)) ?? [];
            var index = list.FindIndex(s => s.ShiftId.Equals(shift.ShiftId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                list[index] = shift;
            }
            else
            {
                list.Add(shift);
            }
            await ManagerJson.WriteAtomicAsync(shiftsPath, list, overwrite: true, token);
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed class JsonTransactionReader(string receiptsDirectory) : ITransactionReader
{
    public async Task<ImmutableArray<Receipt>> ReadAllReceiptsAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!Directory.Exists(receiptsDirectory))
            return [];

        var files = Directory.GetFiles(receiptsDirectory, "*.json", SearchOption.TopDirectoryOnly);
        var receipts = new List<Receipt>();

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (!Guid.TryParseExact(fileName, "N", out var orderId))
                continue;

            try
            {
                var receipt = await ManagerJson.ReadAsync<Receipt>(file, token);
                if (receipt is not null)
                {
                    CartRules.ValidateReceipt(receipt);
                    receipts.Add(receipt);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Unreadable or corrupt receipts are skipped safely
            }
        }

        return receipts.OrderByDescending(r => r.PaidAt).ToImmutableArray();
    }
}

public sealed class ManagerCashierSessionProvider(ManagerController manager) : ICashierSessionProvider
{
    public async Task<CashierSession> GetCurrentAsync(CancellationToken token = default)
    {
        var user = manager.CurrentUser;
        var shift = await manager.GetCurrentOpenShiftAsync(token);

        if (user is null)
        {
            return new CashierSession(
                StaffId: "NONE",
                StaffName: "Chưa đăng nhập",
                ShiftId: "NONE",
                CanSell: false,
                ShiftOpen: false,
                IsDemo: false);
        }

        return new CashierSession(
            StaffId: user.StaffId,
            StaffName: user.FullName,
            ShiftId: shift?.ShiftId ?? "CHUA-MO-CA",
            CanSell: user.CanSell,
            ShiftOpen: shift is not null && shift.Status == ShiftStatus.Open,
            IsDemo: false);
    }
}
