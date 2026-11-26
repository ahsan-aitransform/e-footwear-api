using EFootwear.BLL.Commissions.Abstractions;
using EFootwear.BLL.Commissions.Contracts;
using EFootwear.BLL.Core.Services;
using EFootwear.Domain.Commissions;

namespace EFootwear.BLL.Commissions.Services;

/// <summary>
/// The Commission Report (docx 8.14: store, salesman, from, to; summary or detail) and the Commission Accrual and Payment report (Part 22). Both answer from the
/// commission entries only, so the summary equals the commission the salary sheet takes for the same period. The store filter is checked against the caller's
/// stores; with no store the database limits the answer to the stores they may see.
/// </summary>
public sealed class CommissionReportService(ICommissionReportRepository commissionReportRepository, StoreAccessGuard storeAccessGuard)
{
    public async Task<CommissionSummaryReportResponse> GetSummaryAsync(CommissionReportRequest request, CancellationToken cancellationToken)
    {
        await EnsureRequestIsValidAsync(request, cancellationToken);
        return await commissionReportRepository.GetSummaryAsync(request, CommissionRules.ReportRowLimit, cancellationToken);
    }

    public async Task<CommissionDetailReportResponse> GetDetailAsync(CommissionReportRequest request, CancellationToken cancellationToken)
    {
        await EnsureRequestIsValidAsync(request, cancellationToken);
        return await commissionReportRepository.GetDetailAsync(request, CommissionRules.ReportRowLimit, cancellationToken);
    }

    private async Task EnsureRequestIsValidAsync(CommissionReportRequest request, CancellationToken cancellationToken)
    {
        CommissionRules.EnsureDateRangeIsValid(request.FromDate, request.ToDate);
        if (request.StoreId is { } storeId) await storeAccessGuard.EnsureAccessibleAsync(storeId, cancellationToken);
    }
}
