namespace EFootwear.BLL.Payroll.Contracts;

/// <summary>An incentive, deduction or advance entered for an employee and a month. It goes onto the salary sheet the next time the draft is built (<paramref name="IsOnSalarySheet"/>).</summary>
public sealed record PayrollAdjustmentResponse(
    Guid Id,
    Guid EmployeePartyId,
    string EmployeeName,
    DateOnly PeriodMonth,
    string AdjustmentTypeCode,
    long Amount,
    string? Reason,
    bool IsOnSalarySheet,
    DateTimeOffset CreatedAt);
