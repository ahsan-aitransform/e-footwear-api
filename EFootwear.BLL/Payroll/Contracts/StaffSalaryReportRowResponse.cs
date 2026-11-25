namespace EFootwear.BLL.Payroll.Contracts;

/// <summary>One employee-month of the Staff Salary report (a line of a salary sheet that is not cancelled).</summary>
public sealed record StaffSalaryReportRowResponse(
    DateOnly PeriodMonth,
    Guid StoreId,
    string StoreName,
    Guid StaffId,
    string EmployeeCode,
    string EmployeeName,
    string? Designation,
    long BaseSalary,
    long CommissionAmount,
    long IncentiveAmount,
    long DeductionAmount,
    long NetPay,
    long PaidAmount,
    long OutstandingAmount,
    string StatusCode);
