using System.Collections.Immutable;
using Cashier.Core;

namespace Manager.Core;

public interface ICatalogStore
{
    Task<ImmutableArray<MenuItem>> GetMenuAsync(CancellationToken token = default);
    Task SaveMenuAsync(ImmutableArray<MenuItem> menu, CancellationToken token = default);
    Task DeleteMenuItemAsync(string itemId, CancellationToken token = default);
    Task<ImmutableArray<string>> GetCategoriesAsync(CancellationToken token = default);
    Task CreateCategoryAsync(string category, CancellationToken token = default);
    Task RenameCategoryAsync(string oldName, string newName, CancellationToken token = default);
    Task DeleteCategoryAsync(string category, CancellationToken token = default);

    Task<ImmutableArray<Promotion>> GetPromotionsAsync(CancellationToken token = default);
    Task SavePromotionsAsync(ImmutableArray<Promotion> promotions, CancellationToken token = default);
}

public interface IAccountStore
{
    Task<ImmutableArray<StaffAccount>> GetAccountsAsync(CancellationToken token = default);
    Task SaveAccountsAsync(ImmutableArray<StaffAccount> accounts, CancellationToken token = default);
}

public interface IShiftStore
{
    Task<ImmutableArray<ShiftRecord>> GetShiftsAsync(CancellationToken token = default);
    Task<ShiftRecord?> GetCurrentOpenShiftAsync(CancellationToken token = default);
    Task SaveShiftAsync(ShiftRecord shift, CancellationToken token = default);
}

public interface ITransactionReader
{
    Task<ImmutableArray<Receipt>> ReadAllReceiptsAsync(CancellationToken token = default);
}

