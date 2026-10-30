using System.Collections.Immutable;

namespace Cashier.Core;

// Optional read-only history: Mongo/Manager adapters do not need to implement it.
public sealed record ReceiptHistoryResult(ImmutableArray<Receipt> Receipts, int UnreadableFiles);
public interface IReceiptHistory
{
    Task<ReceiptHistoryResult> ReadRecentAsync(int limit = 100, CancellationToken token = default);
}
