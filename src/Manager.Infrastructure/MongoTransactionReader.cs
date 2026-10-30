using System.Collections.Immutable;
using Cashier.Core;
using Cashier.Infrastructure;
using Cashier.MongoIntegration;
using Manager.Core;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Manager.Infrastructure;

public sealed class MongoTransactionReader : ITransactionReader
{
    private readonly IMongoCollection<BsonDocument> _orders;
    private readonly IGroupOrderMapper _mapper;

    public MongoTransactionReader(IMongoDatabase database, IGroupOrderMapper mapper)
    {
        _orders = database.GetCollection<BsonDocument>("orders");
        _mapper = mapper;
    }

    public async Task<ImmutableArray<Receipt>> ReadAllReceiptsAsync(CancellationToken token = default)
    {
        var documents = await _orders.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(token);
        return documents.Select(_mapper.FromDocument)
            .Where(receipt =>
            {
                CartRules.ValidateReceipt(receipt);
                return true;
            })
            .OrderByDescending(receipt => receipt.PaidAt)
            .ToImmutableArray();
    }
}

public sealed class CombinedTransactionReader(
    ITransactionReader mongoReader,
    ITransactionReader legacyJsonReader) : ITransactionReader
{
    public async Task<ImmutableArray<Receipt>> ReadAllReceiptsAsync(CancellationToken token = default)
    {
        var mongoReceipts = await mongoReader.ReadAllReceiptsAsync(token);
        var legacyReceipts = await legacyJsonReader.ReadAllReceiptsAsync(token);
        return mongoReceipts.Concat(legacyReceipts)
            .GroupBy(receipt => receipt.OrderId)
            .Select(group => group.First())
            .OrderByDescending(receipt => receipt.PaidAt)
            .ToImmutableArray();
    }
}

public sealed class ManagerCashierDataSource(
    ManagerController manager,
    ICashierSessionProvider sessions) : ICashierDataSource
{
    public async Task<SalesData> ReadAsync(CancellationToken token = default) => new(
        await manager.GetMenuAsync(token),
        await manager.GetPromotionsAsync(token),
        await sessions.GetCurrentAsync(token));
}
