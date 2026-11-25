namespace EFootwear.BLL.Payroll.Contracts;

/// <summary>
/// One employee's line on a monthly salary sheet (docx 3.8, 3.9). Net pay = base salary + commission + incentive - deduction; outstanding = net pay - paid.
/// Both are computed by the database. T Sale is the employee's net sales of the month. Money is whole rupees.
/// </summary>
public sealed record PayrollSheetLineResponse(
    Guid LineId,
    Guid StaffId,
    string EmployeeCode,
    string EmployeeName,
    string? Designation,
    Guid StoreId,
    string StoreName,
    long BaseSalary,
    long TotalSales,
    long CommissionAmount,
    long IncentiveAmount,
    long DeductionAmount,
    long NetPay,
    long PaidAmount,
    long OutstandingAmount,
    string StatusCode);
