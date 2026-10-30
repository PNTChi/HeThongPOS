using System.Collections.Immutable;
using Cashier.Core;
using MongoDB.Bson;

namespace Cashier.MongoIntegration;

public sealed class CashierOrderMapper : IGroupOrderMapper
{
    public BsonValue DatabaseId(Guid orderId) => new BsonString(orderId.ToString("D"));

    public BsonDocument ToDocument(Receipt receipt) => new()
    {
        { "_id", DatabaseId(receipt.OrderId) },
        { "OrderId", receipt.OrderId.ToString("D") },
        { "ReceiptNumber", receipt.ReceiptNumber },
        { "PaidAt", receipt.PaidAt.ToString("O") },
        { "StaffId", receipt.StaffId },
        { "StaffName", receipt.StaffName },
        { "ShiftId", receipt.ShiftId },
        { "Lines", new BsonArray(receipt.Lines.Select(line => new BsonDocument
        {
            { "LineId", line.LineId.ToString("D") },
            { "ProductId", line.ProductId },
            { "ProductName", line.ProductName },
            { "BasePrice", line.BasePrice },
            { "Options", new BsonArray(line.Options.Select(option => new BsonDocument
            {
                { "GroupId", option.GroupId },
                { "GroupName", option.GroupName },
                { "ChoiceId", option.ChoiceId },
                { "ChoiceName", option.ChoiceName },
                { "ExtraPrice", option.ExtraPrice }
            })) },
            { "Quantity", line.Quantity }
        })) },
        { "Promotion", receipt.Promotion is null ? BsonNull.Value : new BsonDocument
        {
            { "Code", receipt.Promotion.Code },
            { "Description", receipt.Promotion.Description },
            { "Kind", receipt.Promotion.Kind.ToString() },
            { "Value", receipt.Promotion.Value },
            { "Discount", receipt.Promotion.Discount },
            { "DiscountableSubtotal", receipt.Promotion.DiscountableSubtotal is null
                ? BsonNull.Value
                : new BsonDecimal128(receipt.Promotion.DiscountableSubtotal.Value) }
        } },
        { "Subtotal", receipt.Totals.Subtotal },
        { "Discount", receipt.Totals.Discount },
        { "Payable", receipt.Totals.Payable },
        { "PaymentMethod", receipt.Payment.Method.ToString() },
        { "AmountReceived", receipt.Payment.AmountReceived },
        { "Change", receipt.Payment.Change },
        { "IsDemo", receipt.IsDemo }
    };

    public Receipt FromDocument(BsonDocument document) => new(
        OrderId: Guid.Parse(document["OrderId"].AsString),
        ReceiptNumber: document["ReceiptNumber"].AsString,
        PaidAt: DateTimeOffset.Parse(document["PaidAt"].AsString),
        StaffId: document["StaffId"].AsString,
        StaffName: document["StaffName"].AsString,
        ShiftId: document["ShiftId"].AsString,
        Lines: document["Lines"].AsBsonArray.Select(value =>
        {
            var line = value.AsBsonDocument;
            return new CartLine(
                LineId: Guid.Parse(line["LineId"].AsString),
                ProductId: line["ProductId"].AsString,
                ProductName: line["ProductName"].AsString,
                BasePrice: ToDecimal(line["BasePrice"]),
                Options: line["Options"].AsBsonArray.Select(optionValue =>
                {
                    var option = optionValue.AsBsonDocument;
                    return new SelectedOption(
                        GroupId: option["GroupId"].AsString,
                        GroupName: option["GroupName"].AsString,
                        ChoiceId: option["ChoiceId"].AsString,
                        ChoiceName: option["ChoiceName"].AsString,
                        ExtraPrice: ToDecimal(option["ExtraPrice"]));
                }).ToImmutableArray(),
                Quantity: line["Quantity"].AsInt32);
        }).ToImmutableArray(),
        Promotion: document["Promotion"].IsBsonNull ? null : new AppliedPromotion(
            Code: document["Promotion"]["Code"].AsString,
            Description: document["Promotion"]["Description"].AsString,
            Kind: Enum.Parse<DiscountKind>(document["Promotion"]["Kind"].AsString),
            Value: ToDecimal(document["Promotion"]["Value"]),
            Discount: ToDecimal(document["Promotion"]["Discount"]),
            DiscountableSubtotal: document["Promotion"].AsBsonDocument.TryGetValue("DiscountableSubtotal", out var discountable) &&
                !discountable.IsBsonNull ? ToDecimal(discountable) : null),
        Totals: new CartTotals(
            Subtotal: ToDecimal(document["Subtotal"]),
            Discount: ToDecimal(document["Discount"]),
            Payable: ToDecimal(document["Payable"])),
        Payment: new PaymentSnapshot(
            Method: Enum.Parse<PaymentMethod>(document["PaymentMethod"].AsString),
            AmountReceived: ToDecimal(document["AmountReceived"]),
            Change: ToDecimal(document["Change"])),
        IsDemo: document["IsDemo"].AsBoolean);

    private static decimal ToDecimal(BsonValue value) => value.BsonType switch
    {
        BsonType.Int32 => value.AsInt32,
        BsonType.Int64 => value.AsInt64,
        BsonType.Double => (decimal)value.AsDouble,
        BsonType.Decimal128 => (decimal)value.AsDecimal,
        _ => throw new InvalidOperationException($"Cannot convert {value.BsonType} to decimal")
    };
}
