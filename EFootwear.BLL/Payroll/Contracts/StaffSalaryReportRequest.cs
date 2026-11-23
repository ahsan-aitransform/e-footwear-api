namespace EFootwear.BLL.Payroll.Contracts;

/// <summary>Filters of the Staff Salary report: a store (empty = every store the user may see), a range of months and optionally one employee.</summary>
public sealed record StaffSalaryReportRequest(Guid? StoreId, DateOnly FromMonth, DateOnly ToMonth, Guid? EmployeePartyId);
