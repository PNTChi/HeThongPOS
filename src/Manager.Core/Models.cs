using System.Collections.Immutable;
using Cashier.Core;

namespace Manager.Core;

public enum UserRole
{
    Manager,
    Cashier
}

public sealed record StaffAccount(
    string StaffId,
    string FullName,
    string Username,
    string Password,
    UserRole Role,
    bool IsActive = true,
    bool CanSell = true,
    bool CanManageShift = true);

public enum ShiftStatus
{
    Open,
    Closed
}

public sealed record ShiftRecord(
    string ShiftId,
    string StaffId,
    string StaffName,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    decimal StartingCash,
    decimal? ActualCash,
    decimal ExpectedCash,
    decimal ExpectedTransfer,
    decimal Difference,
    string? Notes,
    ShiftStatus Status);

public sealed record ShiftReconciliation(
    string ShiftId,
    string StaffId,
    string StaffName,
    DateTimeOffset OpenedAt,
    DateTimeOffset ClosedAt,
    int TotalOrders,
    decimal TotalRevenue,
    int CashOrdersCount,
    decimal CashTotal,
    int TransferOrdersCount,
    decimal TransferTotal,
    decimal StartingCash,
    decimal ActualCash,
    decimal ExpectedCash,
    decimal Difference,
    string? Notes);

public sealed record HistoryFilter(
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null,
    string? StaffQuery = null,
    string? ShiftQuery = null,
    PaymentMethod? Method = null,
    string? Keyword = null);

public enum ReportKind
{
    RevenueByPeriod,
    RevenueByShift,
    TopProducts,
    PaymentBreakdown
}

public sealed record ReportMetric(
    string Indicator,
    int Quantity,
    decimal Value,
    string Period);

public sealed record ProductSalesStat(
    string ProductId,
    string ProductName,
    string Category,
    int QuantitySold,
    decimal Revenue);

public sealed record ReportSummary(
    ReportKind Kind,
    DateTimeOffset FromDate,
    DateTimeOffset ToDate,
    int TotalOrders,
    decimal TotalRevenue,
    decimal TotalDiscount,
    decimal CashTotal,
    decimal TransferTotal,
    ImmutableArray<ReportMetric> Metrics,
    ImmutableArray<ProductSalesStat> TopProducts);

