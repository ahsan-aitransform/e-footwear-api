namespace EFootwear.BLL.Payroll.Contracts;

/// <summary>What the payment use case needs to know about a salary line before it pays: whose line, which store, its status and what is still owed.</summary>
public sealed record PayrollLineSummary(Guid LineId, Guid StoreId, Guid StaffId, DateOnly PeriodMonth, string StatusCode, long OutstandingAmount);
