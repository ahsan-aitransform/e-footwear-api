namespace EFootwear.BLL.Payroll.Contracts;

/// <summary>An approved salary line that is not paid in full: net pay, what was paid and what is still outstanding.</summary>
public sealed record SalaryOutstandingReportRowResponse(
    DateOnly PeriodMonth,
    Guid StoreId,
    string StoreName,
    Guid StaffId,
    string EmployeeName,
    long NetPay,
    long PaidAmount,
    long OutstandingAmount);
