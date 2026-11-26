using EFootwear.BLL.Commissions.Contracts;

namespace EFootwear.BLL.Commissions.Abstractions;

/// <summary>
/// The commission reports (docx 8.14 Commission Report, ADD-22-09 Commission Accrual and Payment). Both read hr.commission_entries one row per entry, so no join
/// can multiply a figure. Cancelled entries are left out. The totals are taken over every matching entry, not only the rows returned.
/// </summary>
public interface ICommissionReportRepository
{
    Task<CommissionSummaryReportResponse> GetSummaryAsync(CommissionReportRequest request, int rowLimit, CancellationToken cancellationToken);

    Task<CommissionDetailReportResponse> GetDetailAsync(CommissionReportRequest request, int rowLimit, CancellationToken cancellationToken);
}
