namespace EFootwear.BLL.Accounting.Contracts;

/// <summary><c>PeriodStart</c> is any date inside the month to close or reopen; the service uses the first day of that month.</summary>
public sealed record ChangeAccountingPeriodRequest(DateOnly PeriodStart);
