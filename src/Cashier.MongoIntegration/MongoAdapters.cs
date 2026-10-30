using System.Collections.Immutable;
using Cashier.Core;
using Cashier.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Cashier.MongoIntegration;

// Physical mapping is supplied by the group, not guessed by the Cashier module.
public interface IGroupOrderMapper
{
    BsonValue DatabaseId(Guid orderId);
    BsonDocument ToDocument(Receipt receipt);
    Receipt FromDocument(BsonDocument document);
}

public sealed class MongoOrderRepository : IOrderRepository
{
    private readonly IMongoCollection<BsonDocument> _orders;
    private readonly IGroupOrderMapper _mapper;
    public MongoOrderRepository(IMongoCollection<BsonDocument> approvedCollection, IGroupOrderMapper mapper)
    {
        // This adapter never creates collections/indexes or updates Manager shift balances.
        // A stable _id relies on MongoDB's built-in unique _id index for idempotency.
        _orders = approvedCollection.WithReadPreference(ReadPreference.Primary)
            .WithWriteConcern(WriteConcern.W1.With(journal: true));
        _mapper = mapper;
    }
    public async Task<Receipt?> FindAsync(Guid orderId, CancellationToken token = default)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", _mapper.DatabaseId(orderId));
        var document = await _orders.Find(filter).FirstOrDefaultAsync(token);
        if (document is null) return null;
        var receipt = _mapper.FromDocument(document);
        CartRules.ValidateReceipt(receipt);
        if (receipt.OrderId != orderId) throw new IOException("Ánh xạ mã giao dịch MongoDB không khớp.");
        return receipt;
    }
    // A primary read returning NOT FOUND cannot prove a timed-out insert won't commit later.
    // No release-to-new-identity is allowed until the approved integration can prove a final outcome.
    public Task<bool> CanReleaseUnpaidAsync(Guid orderId, CancellationToken token = default) => Task.FromResult(false);
    public async Task<Receipt> SaveAsync(Receipt receipt, CancellationToken token = default)
    {
        CartRules.ValidateReceipt(receipt);
        using (var names = await _orders.Database.ListCollectionNamesAsync(
            new ListCollectionNamesOptions { Filter = new BsonDocument("name", _orders.CollectionNamespace.CollectionName) }, token))
        {
            if (!(await names.ToListAsync(token)).Contains(_orders.CollectionNamespace.CollectionName))
                throw new BusinessException("Collection chưa tồn tại. Cashier không tự tạo collection/schema; cần dùng collection đã được Chi thiết lập.");
        }
        var existing = await FindAsync(receipt.OrderId, token);
        if (existing is not null) return RequireSame(existing, receipt);
        var document = _mapper.ToDocument(receipt);
        if (!document.TryGetValue("_id", out var physicalId) || physicalId != _mapper.DatabaseId(receipt.OrderId))
            throw new BusinessException("Mapper phải dùng _id ổn định cho cùng một giao dịch.");
        try { await _orders.InsertOneAsync(document, cancellationToken: token); }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            existing = await FindAsync(receipt.OrderId, token);
            if (existing is null) throw;
            return RequireSame(existing, receipt);
        }
        return RequireSame(await FindAsync(receipt.OrderId, token) ??
            throw new IOException("Chưa xác nhận được giao dịch vừa ghi MongoDB."), receipt);
    }
    private static Receipt RequireSame(Receipt a, Receipt b) => ReceiptIdentity.Same(a, b) ? a :
        throw new IOException("Mã giao dịch đã tồn tại với nội dung khác. Không ghi đè.");
}

public sealed class MongoReceiptHistory(
    IMongoCollection<BsonDocument> approvedOrders,
    IGroupOrderMapper mapper) : IReceiptHistory
{
    public async Task<ReceiptHistoryResult> ReadRecentAsync(
        int limit = 100,
        CancellationToken token = default)
    {
        var documents = await approvedOrders
            .WithReadPreference(ReadPreference.Primary)
            .Find(Builders<BsonDocument>.Filter.Empty)
            .ToListAsync(token);

        var receipts = ImmutableArray.CreateBuilder<Receipt>();
        var unreadableFiles = 0;
        foreach (var document in documents)
        {
            try
            {
                var receipt = mapper.FromDocument(document);
                CartRules.ValidateReceipt(receipt);
                receipts.Add(receipt);
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                unreadableFiles++;
            }
        }

        return new ReceiptHistoryResult(
            receipts.OrderByDescending(receipt => receipt.PaidAt)
                .Take(Math.Max(0, limit))
                .ToImmutableArray(),
            unreadableFiles);
    }
}

public sealed class MongoSalesDataSource(
    IMongoCollection<BsonDocument> approvedCatalog,
    IMongoCollection<BsonDocument> approvedPromotions,
    Func<BsonDocument, MenuItem> mapProduct,
    Func<BsonDocument, Promotion> mapPromotion,
    ICashierSessionProvider sessions) : ICashierDataSource
{
    public async Task<SalesData> ReadAsync(CancellationToken token = default)
    {
        var products = await approvedCatalog.WithReadPreference(ReadPreference.Primary)
            .Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(token);
        var promotions = await approvedPromotions.WithReadPreference(ReadPreference.Primary)
            .Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(token);
        return new(products.Select(mapProduct).ToImmutableArray(), promotions.Select(mapPromotion).ToImmutableArray(),
            await sessions.GetCurrentAsync(token));
    }
}
