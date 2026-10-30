using System.Collections.Immutable;
using Cashier.Core;

namespace Cashier.Infrastructure;

public sealed record CatalogData(ImmutableArray<MenuItem> Menu, ImmutableArray<Promotion> Promotions);

// Integration seam for Nhi's authenticated session and open shift (not a login/shift management UI).
public interface ICashierSessionProvider
{
    Task<CashierSession> GetCurrentAsync(CancellationToken token = default);
}
public sealed class DemoSessionProvider : ICashierSessionProvider
{
    public Task<CashierSession> GetCurrentAsync(CancellationToken token = default) =>
        Task.FromResult(new CashierSession("DEMO-KHOI", "Nguyễn Tuấn Khôi", "DEMO-CA", true, true, true));
}
public sealed class JsonSalesDataSource(string catalogPath, ICashierSessionProvider sessions) : ICashierDataSource
{
    public async Task<SalesData> ReadAsync(CancellationToken token = default)
    {
        var catalog = await BusinessJson.ReadAsync<CatalogData>(catalogPath, token)
            ?? throw new IOException("Không tìm thấy dữ liệu thực đơn: " + catalogPath);
        if (catalog.Menu.IsDefaultOrEmpty || catalog.Promotions.IsDefault)
            throw new IOException("Dữ liệu mẫu thiếu thực đơn hoặc danh sách khuyến mãi.");
        if (catalog.Menu.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != catalog.Menu.Length ||
            catalog.Promotions.Select(x => x.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() != catalog.Promotions.Length)
            throw new IOException("Thực đơn/mã khuyến mãi bị trùng mã trong dữ liệu cấu hình.");
        return new(catalog.Menu, catalog.Promotions, await sessions.GetCurrentAsync(token));
    }
}
