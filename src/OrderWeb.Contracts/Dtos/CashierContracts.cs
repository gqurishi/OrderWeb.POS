namespace OrderWeb.Contracts.Dtos;

// Transport-only contracts for the Mother-authoritative Client Cashier API.
public sealed record CashierActionRequestDto(string RequestId, string? SessionToken = null, string? Reason = null, string? PrinterTarget = null);
public sealed record CashierActionResponseDto(bool Success, string Message, string? PrinterName, string? ReportReference, DateTimeOffset OccurredUtc);
public sealed record CashierDashboardResponseDto(bool Success, DateTime BusinessDate, int TotalOrders, decimal TotalSales, decimal CashTotal, decimal CardTotal, decimal OtherPaymentTotal, int VoidCount, decimal VoidAmount, decimal DiscountTotal, decimal ExpectedCash, decimal? CountedCash, decimal? Variance, string TerminalName, DateTimeOffset GeneratedUtc, string Version);
