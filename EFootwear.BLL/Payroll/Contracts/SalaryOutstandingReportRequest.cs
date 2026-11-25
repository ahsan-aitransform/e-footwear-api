namespace EFootwear.BLL.Payroll.Contracts;

/// <summary>Filters of the Salary Outstanding report: a store (empty = every store the user may see) and optionally one employee.</summary>
public sealed record SalaryOutstandingReportRequest(Guid? StoreId, Guid? StaffId);
