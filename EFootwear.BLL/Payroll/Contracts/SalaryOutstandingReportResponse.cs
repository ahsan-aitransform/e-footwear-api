namespace EFootwear.BLL.Payroll.Contracts;

/// <summary>The Salary Outstanding report: salary that is approved (owed) and not yet paid, with totals over every matching line.</summary>
public sealed record SalaryOutstandingReportResponse(
    IReadOnlyList<SalaryOutstandingReportRowResponse> Rows,
    long TotalNetPay,
    long TotalPaid,
    long TotalOutstanding,
    long LineCount,
    bool IsTruncated);
