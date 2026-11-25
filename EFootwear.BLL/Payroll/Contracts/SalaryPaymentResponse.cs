namespace EFootwear.BLL.Payroll.Contracts;

/// <summary>One recorded salary payment (hr.salary_payments, append-only) with the month and the employee it belongs to.</summary>
public sealed record SalaryPaymentResponse(
    Guid Id,
    Guid PayrollLineId,
    Guid StaffId,
    string EmployeeName,
    Guid StoreId,
    string StoreName,
    DateOnly PeriodMonth,
    DateOnly PaidOn,
    long Amount,
    string MethodCode,
    string? Reference,
    Guid? PaidByUserId,
    string? PaidByName,
    DateTimeOffset CreatedAt);
