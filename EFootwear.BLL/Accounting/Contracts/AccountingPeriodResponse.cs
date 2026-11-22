namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>One month of the books. A month without a stored row is open.</summary>
public sealed record AccountingPeriodResponse(DateOnly PeriodStart, string StatusCode, DateTimeOffset? ClosedAt, string? ClosedByName);
