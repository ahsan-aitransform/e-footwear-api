namespace EFootwear.BLL.Payroll.Contracts;

/// <summary>Enters an incentive, deduction or advance (whole rupees, more than zero) for a staff member and a month.</summary>
public sealed record CreatePayrollAdjustmentRequest(Guid EmployeePartyId, DateOnly Month, string AdjustmentTypeCode, long Amount, string? Reason);
