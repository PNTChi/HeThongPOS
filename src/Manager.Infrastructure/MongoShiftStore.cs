using System.Collections.Immutable;
using Manager.Core;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Manager.Infrastructure;

public sealed class MongoShiftStore : IShiftStore
{
    private readonly IMongoCollection<BsonDocument> _shifts;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MongoShiftStore(IMongoDatabase database)
    {
        _shifts = database.GetCollection<BsonDocument>("shifts");
    }

    public async Task<ImmutableArray<ShiftRecord>> GetShiftsAsync(
        CancellationToken token = default)
    {
        await _gate.WaitAsync(token);

        try
        {
            var documents = await _shifts
                .Find(Builders<BsonDocument>.Filter.Empty)
                .ToListAsync(token);

            return documents
                .Select(ToShiftRecord)
                .ToImmutableArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ShiftRecord?> GetCurrentOpenShiftAsync(
        CancellationToken token = default)
    {
        var shifts = await GetShiftsAsync(token);
        return shifts.FirstOrDefault(s => s.Status == ShiftStatus.Open);
    }

    public async Task SaveShiftAsync(
        ShiftRecord shift,
        CancellationToken token = default)
    {
        await _gate.WaitAsync(token);

        try
        {
            var filter = Builders<BsonDocument>.Filter.Eq("_id", shift.ShiftId);
            var document = ToShiftDocument(shift);

            await _shifts.ReplaceOneAsync(
                filter,
                document,
                new ReplaceOptions { IsUpsert = true },
                token);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static ShiftRecord ToShiftRecord(BsonDocument document)
    {
        return new ShiftRecord(
            ShiftId: document["ShiftId"].AsString,
            StaffId: document["StaffId"].AsString,
            StaffName: document["StaffName"].AsString,
            OpenedAt: DateTimeOffset.Parse(document["OpenedAt"].AsString),
            ClosedAt: document.TryGetValue("ClosedAt", out var closedAt) && closedAt.IsString
                ? DateTimeOffset.Parse(closedAt.AsString)
                : null,
            StartingCash: ToDecimal(document["StartingCash"]),
            ActualCash: document.TryGetValue("ActualCash", out var actualCash) && !actualCash.IsBsonNull
                ? ToDecimal(actualCash)
                : null,
            ExpectedCash: ToDecimal(document["ExpectedCash"]),
            ExpectedTransfer: ToDecimal(document["ExpectedTransfer"]),
            Difference: ToDecimal(document["Difference"]),
            Notes: document.TryGetValue("Notes", out var notes) && notes.IsString ? notes.AsString : null,
            Status: Enum.Parse<ShiftStatus>(
                document["Status"].AsString,
                ignoreCase: true));
    }

    private static BsonDocument ToShiftDocument(ShiftRecord shift)
    {
        var doc = new BsonDocument
        {
            { "_id", shift.ShiftId },
            { "ShiftId", shift.ShiftId },
            { "StaffId", shift.StaffId },
            { "StaffName", shift.StaffName },
            { "OpenedAt", shift.OpenedAt.ToString("O") },
            { "StartingCash", shift.StartingCash },
            { "ExpectedCash", shift.ExpectedCash },
            { "ExpectedTransfer", shift.ExpectedTransfer },
            { "Difference", shift.Difference },
            { "Status", shift.Status.ToString() }
        };

        if (shift.ClosedAt.HasValue)
        {
            doc["ClosedAt"] = shift.ClosedAt.Value.ToString("O");
        }

        if (shift.ActualCash.HasValue)
        {
            doc["ActualCash"] = shift.ActualCash.Value;
        }

        if (!string.IsNullOrEmpty(shift.Notes))
        {
            doc["Notes"] = shift.Notes;
        }

        return doc;
    }

    private static decimal ToDecimal(BsonValue value)
    {
        return value.BsonType switch
        {
            BsonType.Int32 => value.AsInt32,
            BsonType.Int64 => value.AsInt64,
            BsonType.Double => (decimal)value.AsDouble,
            BsonType.Decimal128 => (decimal)value.AsDecimal,
            _ => throw new InvalidOperationException($"Cannot convert {value.BsonType} to decimal")
        };
    }
}
