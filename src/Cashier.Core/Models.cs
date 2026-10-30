using System.Collections.Immutable;

namespace Cashier.Core;

// These are business contracts, NOT MongoDB collection/field definitions.
public sealed record OptionChoice(string Id, string Name, decimal ExtraPrice, bool Available = true);
public sealed record OptionGroup(string Id, string Name, int Minimum, int Maximum,
    ImmutableArray<OptionChoice> Choices);
public sealed record MenuItem(string Id, string Name, string Category, decimal BasePrice,
    bool Available, ImmutableArray<OptionGroup> OptionGroups)
{
    // Optional, read-only Manager metadata. Relative to the application's Data folder,
    // for example "Images/ca-phe-sua.png". Existing catalogs need no image field.
    public string? ImagePath { get; init; }
    // Optional base64 encoded image bytes stored alongside the product in MongoDB.
    public string? ImageData { get; init; }
}
public enum DiscountKind { Percentage, FixedAmount }
public sealed record Promotion(string Code, string Description, DiscountKind Kind, decimal Value,
    decimal MinimumSubtotal, DateTimeOffset StartsAt, DateTimeOffset EndsAt, bool Active,
    ImmutableArray<string> ProductIds = default, ImmutableArray<string> Categories = default);
public sealed record CashierSession(string StaffId, string StaffName, string ShiftId,
    bool CanSell, bool ShiftOpen, bool IsDemo);
public sealed record SalesData(ImmutableArray<MenuItem> Menu, ImmutableArray<Promotion> Promotions,
    CashierSession Session);
public sealed record OptionSelection(string GroupId, string ChoiceId);
public sealed record SelectedOption(string GroupId, string GroupName, string ChoiceId,
    string ChoiceName, decimal ExtraPrice);
public sealed record CartLine(Guid LineId, string ProductId, string ProductName, decimal BasePrice,
    ImmutableArray<SelectedOption> Options, int Quantity)
{
    public decimal UnitPrice => BasePrice + Options.Sum(x => x.ExtraPrice);
    public decimal LineTotal => UnitPrice * Quantity;
    public string OptionText => string.Join(", ", Options.Select(x => $"{x.GroupName}: {x.ChoiceName}"));
}
public sealed record AppliedPromotion(string Code, string Description, DiscountKind Kind,
    decimal Value, decimal Discount, decimal? DiscountableSubtotal = null);
public sealed record CartTotals(decimal Subtotal, decimal Discount, decimal Payable);
public enum PaymentMethod { Cash, BankTransfer }
public sealed record PaymentRequest(PaymentMethod Method, decimal CashReceived = 0,
    bool TransferReceivedConfirmed = false);
public sealed record PaymentSnapshot(PaymentMethod Method, decimal AmountReceived, decimal Change);
public sealed record Receipt(Guid OrderId, string ReceiptNumber, DateTimeOffset PaidAt,
    string StaffId, string StaffName, string ShiftId, ImmutableArray<CartLine> Lines,
    AppliedPromotion? Promotion, CartTotals Totals, PaymentSnapshot Payment, bool IsDemo);
public enum CheckoutState { Ready, Saving, NotSaved, Uncertain, Paid }
public enum CheckoutResultKind { Paid, Rejected, NotSaved, Uncertain }
public sealed record CheckoutResult(CheckoutResultKind Kind, string Message, Receipt? Receipt = null);
public sealed class BusinessException(string message) : Exception(message);

public interface ICashierDataSource
{
    // Must supply read-only Manager-configured catalog/promotions and a legitimate current session.
    Task<SalesData> ReadAsync(CancellationToken cancellationToken = default);
}
public interface IOrderRepository
{
    // Save must be idempotent by OrderId. Find must read the authoritative store.
    Task<Receipt> SaveAsync(Receipt receipt, CancellationToken cancellationToken = default);
    Task<Receipt?> FindAsync(Guid orderId, CancellationToken cancellationToken = default);
    // NOT FOUND alone does not prove a timed-out remote write cannot commit later.
    // Return true only when the store can definitively release this unpaid identity.
    // Conservative default for remote/unknown stores: retain the same pending OrderId.
    Task<bool> CanReleaseUnpaidAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
public interface ICheckoutJournal
{
    Task<Receipt?> ReadAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(Receipt candidate, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
