using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cashier.Core;

namespace Cashier.Infrastructure;

public static class BusinessJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public static async Task<T?> ReadAsync<T>(string path, CancellationToken token = default)
    {
        FileStream stream;
        try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch (FileNotFoundException) { return default; }
        catch (DirectoryNotFoundException) { return default; }
        // Permission, malformed path and other I/O failures must remain errors, not NOT FOUND.
        await using var ownedStream = stream;
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, token)
            ?? throw new IOException("Tệp dữ liệu không chứa nội dung hợp lệ: " + path);
    }

    internal static async Task WriteAtomicAsync<T>(string path, T value, bool overwrite, CancellationToken token)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".writing-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, token);
                await stream.FlushAsync(token);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite); // Same volume; no partially written final receipt.
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

// A local runnable demo adapter only. It does not change the group's MongoDB schema.
public sealed class JsonOrderRepository(string directory) : IOrderRepository, IReceiptHistory
{
    public async Task<ReceiptHistoryResult> ReadRecentAsync(int limit = 100, CancellationToken token = default)
    {
        if (limit is < 1 or > 500) throw new BusinessException("Giới hạn lịch sử phải từ 1 đến 500 hóa đơn.");
        token.ThrowIfCancellationRequested();
        string[] files;
        try { files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly); }
        catch (DirectoryNotFoundException) { return new([], 0); }
        // Folder permission/path errors remain errors, not an empty history.
        var values = new List<Receipt>();
        var unreadable = 0;
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out var id)) continue;
            try
            {
                var receipt = await FindAsync(id, token);
                if (receipt is not null) values.Add(receipt);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { unreadable++; } // Corrupt/unreadable receipts are skipped visibly; never deleted.
        }
        return new(values.OrderByDescending(x => x.PaidAt).ThenBy(x => x.OrderId)
            .Take(limit).ToImmutableArray(), unreadable);
    }

    private string ReceiptPath(Guid id) => Path.Combine(directory, id.ToString("N") + ".json");
    public async Task<Receipt?> FindAsync(Guid orderId, CancellationToken token = default)
    {
        var receipt = await BusinessJson.ReadAsync<Receipt>(ReceiptPath(orderId), token);
        if (receipt is not null)
        {
            CartRules.ValidateReceipt(receipt);
            if (receipt.OrderId != orderId) throw new IOException("Mã giao dịch trong tệp không khớp.");
        }
        return receipt;
    }
    public async Task<Receipt> SaveAsync(Receipt receipt, CancellationToken token = default)
    {
        CartRules.ValidateReceipt(receipt);
        var existing = await FindAsync(receipt.OrderId, token);
        if (existing is not null) return EnsureSame(existing, receipt);
        try { await BusinessJson.WriteAtomicAsync(ReceiptPath(receipt.OrderId), receipt, false, token); }
        catch (IOException) when (File.Exists(ReceiptPath(receipt.OrderId)))
        {
            return EnsureSame((await FindAsync(receipt.OrderId, token))!, receipt);
        }
        return (await FindAsync(receipt.OrderId, token))!;
    }
    public async Task<bool> CanReleaseUnpaidAsync(Guid orderId, CancellationToken token = default) =>
        await FindAsync(orderId, token) is null;
    private static Receipt EnsureSame(Receipt existing, Receipt requested) =>
        ReceiptIdentity.Same(existing, requested) ? existing :
            throw new IOException("Trùng mã nhưng nội dung giao dịch khác. Không ghi đè hóa đơn đã lưu.");
}

public sealed class JsonCheckoutJournal(string path) : ICheckoutJournal
{
    public Task<Receipt?> ReadAsync(CancellationToken token = default) => BusinessJson.ReadAsync<Receipt>(path, token);
    public Task WriteAsync(Receipt candidate, CancellationToken token = default) =>
        BusinessJson.WriteAtomicAsync(path, candidate, true, token);
    public Task ClearAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        File.Delete(path); // Missing file is harmless; access/I/O errors must not be silently ignored.
        return Task.CompletedTask;
    }
}
