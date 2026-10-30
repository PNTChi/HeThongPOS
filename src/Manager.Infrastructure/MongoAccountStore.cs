using System.Collections.Immutable;
using Manager.Core;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Manager.Infrastructure;

public sealed class MongoAccountStore : IAccountStore
{
    private readonly IMongoCollection<BsonDocument> _accounts;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MongoAccountStore(IMongoDatabase database)
    {
        _accounts = database.GetCollection<BsonDocument>("accounts");
    }

    public async Task<ImmutableArray<StaffAccount>> GetAccountsAsync(
        CancellationToken token = default)
    {
        await _gate.WaitAsync(token);

        try
        {
            var documents = await _accounts
                .Find(Builders<BsonDocument>.Filter.Empty)
                .ToListAsync(token);

            return documents
                .Select(ToStaffAccount)
                .ToImmutableArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAccountsAsync(
        ImmutableArray<StaffAccount> accounts,
        CancellationToken token = default)
    {
        await _gate.WaitAsync(token);

        try
        {
            foreach (var account in accounts)
            {
                var filter = Builders<BsonDocument>.Filter.Eq(
                    "_id",
                    account.StaffId);

                var document = ToDocument(account);

                await _accounts.ReplaceOneAsync(
                    filter,
                    document,
                    new ReplaceOptions { IsUpsert = true },
                    token);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static StaffAccount ToStaffAccount(BsonDocument document)
    {
        return new StaffAccount(
            StaffId: document["StaffId"].AsString,
            FullName: document["FullName"].AsString,
            Username: document["Username"].AsString,
            Password: document["Password"].AsString,
            Role: Enum.Parse<UserRole>(
                document["Role"].AsString,
                ignoreCase: true),
            IsActive: document.GetValue("IsActive", true).AsBoolean,
            CanSell: document.GetValue("CanSell", true).AsBoolean,
            CanManageShift: document
                .GetValue("CanManageShift", true)
                .AsBoolean);
    }

    private static BsonDocument ToDocument(StaffAccount account)
    {
        return new BsonDocument
        {
            { "_id", account.StaffId },
            { "StaffId", account.StaffId },
            { "FullName", account.FullName },
            { "Username", account.Username },
            { "Password", account.Password },
            { "Role", account.Role.ToString() },
            { "IsActive", account.IsActive },
            { "CanSell", account.CanSell },
            { "CanManageShift", account.CanManageShift }
        };
    }
}