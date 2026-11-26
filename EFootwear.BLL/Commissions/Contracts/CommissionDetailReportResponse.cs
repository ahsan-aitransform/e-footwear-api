namespace EFootwear.BLL.Commissions.Contracts;

/// <summary>The detail of the commission report: one row per entry (one per invoice line and staff member), and totals over every matching entry.</summary>
public sealed record CommissionDetailReportResponse(
    IReadOnlyList<CommissionEntryResponse> Rows,
    long TotalBaseAmount,
    long TotalCommissionAmount,
    long EntryCount,
    bool IsRowLimitReached);
