using MongoDB.Bson;

namespace Cashier.WinForms;

internal static class MongoUtils
{
    public static decimal ToBsonDecimal(BsonValue value) => value.BsonType switch
    {
        BsonType.Int32 => value.AsInt32,
        BsonType.Int64 => value.AsInt64,
        BsonType.Double => (decimal)value.AsDouble,
        BsonType.Decimal128 => (decimal)value.AsDecimal,
        _ => throw new InvalidOperationException($"Cannot convert {value.BsonType} to decimal")
    };
}
