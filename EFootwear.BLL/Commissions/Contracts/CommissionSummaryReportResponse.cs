namespace EFootwear.BLL.Commissions.Contracts;

/// <summary>The summary of the commission report: one row per staff member, and totals over every matching staff member (not only the rows returned).</summary>
public sealed record CommissionSummaryReportResponse(
    IReadOnlyList<CommissionSummaryRowResponse> Rows,
    long TotalBaseAmount,
    long TotalEarnedAmount,
    long TotalReversedAmount,
    long TotalNetAmount,
    long TotalPendingAmount,
    long TotalApprovedAmount,
    long TotalPaidAmount,
    long StaffCount,
    bool IsRowLimitReached);
