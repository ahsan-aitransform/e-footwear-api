namespace EFootwear.BLL.Payroll.Contracts;

/// <summary>
/// The Staff Salary report. The totals cover every matching line, not only the rows returned, so a report cut at the row limit still shows the right figures
/// (<paramref name="IsTruncated"/> says it was cut). A draft line counts in the totals as planned pay; only approved and paid lines are owed.
/// </summary>
public sealed record StaffSalaryReportResponse(
    IReadOnlyList<StaffSalaryReportRowResponse> Rows,
    long TotalBaseSalary,
    long TotalCommission,
    long TotalIncentive,
    long TotalDeduction,
    long TotalNetPay,
    long TotalPaid,
    long TotalOutstanding,
    long LineCount,
    bool IsTruncated);
