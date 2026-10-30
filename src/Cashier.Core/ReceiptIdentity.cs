namespace Cashier.Core;

public static class ReceiptIdentity
{
    public static bool Same(Receipt a, Receipt b) =>
        a.OrderId == b.OrderId && a.ReceiptNumber == b.ReceiptNumber && a.PaidAt == b.PaidAt &&
        a.StaffId == b.StaffId && a.StaffName == b.StaffName && a.ShiftId == b.ShiftId &&
        a.Promotion == b.Promotion && a.Totals == b.Totals && a.Payment == b.Payment && a.IsDemo == b.IsDemo &&
        a.Lines.Length == b.Lines.Length && a.Lines.Zip(b.Lines).All(pair => SameLine(pair.First, pair.Second));

    private static bool SameLine(CartLine a, CartLine b) => a.LineId == b.LineId && a.ProductId == b.ProductId &&
        a.ProductName == b.ProductName && a.BasePrice == b.BasePrice && a.Quantity == b.Quantity &&
        a.Options.SequenceEqual(b.Options);
}
