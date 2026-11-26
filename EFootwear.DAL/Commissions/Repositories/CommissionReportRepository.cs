using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Commissions.Abstractions;
using EFootwear.BLL.Commissions.Contracts;
using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Commissions.Repositories;

/// <summary>
/// The commission reports. Both read hr.commission_entries one row per entry (shared SQL with the entries list), so the summary of a period equals the commission
/// the salary sheet takes for it. Cancelled entries are left out. The totals are taken over every matching entry, not only the rows returned.
/// </summary>
public sealed class CommissionReportRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : ICommissionReportRepository
{
    public async Task<CommissionSummaryReportResponse> GetSummaryAsync(CommissionReportRequest request, int rowLimit, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var filterParameters = BuildFilterParameters(request);

        var totals = await databaseContext.Database.SqlQueryRaw<SummaryTotalsRow>($$"""
            select cast(coalesce(sum(e.base_amount), 0) as bigint) as "TotalBaseAmount",
                   cast(coalesce(sum(e.commission_amount) filter (where e.entry_type = 'EARN'), 0) as bigint) as "TotalEarnedAmount",
                   cast(coalesce(sum(e.commission_amount) filter (where e.entry_type = 'REVERSAL'), 0) as bigint) as "TotalReversedAmount",
                   cast(coalesce(sum(e.commission_amount), 0) as bigint) as "TotalNetAmount",
                   cast(coalesce(sum(e.commission_amount) filter (where e.status = 'PENDING'), 0) as bigint) as "TotalPendingAmount",
                   cast(coalesce(sum(e.commission_amount) filter (where e.status in ('APPROVED', 'ON_HOLD')), 0) as bigint) as "TotalApprovedAmount",
                   cast(coalesce(sum(e.commission_amount) filter (where e.status = 'PAID'), 0) as bigint) as "TotalPaidAmount",
                   count(distinct e.staff_id) as "StaffCount"
            {{CommissionEntrySql.FromAndWhere}}
            """, filterParameters).SingleAsync(cancellationToken);

        var parameters = filterParameters.Concat(new object?[] { rowLimit + 1 }).ToArray();
        var rows = await databaseContext.Database.SqlQueryRaw<SummaryRow>($$"""
            select e.staff_id as "StaffId", p.code as "StaffCode", p.name as "StaffName",
                   cast(coalesce(sum(e.base_amount), 0) as bigint) as "BaseAmount",
                   cast(coalesce(sum(e.commission_amount) filter (where e.entry_type = 'EARN'), 0) as bigint) as "EarnedAmount",
                   cast(coalesce(sum(e.commission_amount) filter (where e.entry_type = 'REVERSAL'), 0) as bigint) as "ReversedAmount",
                   cast(coalesce(sum(e.commission_amount), 0) as bigint) as "NetAmount",
                   cast(coalesce(sum(e.commission_amount) filter (where e.status = 'PENDING'), 0) as bigint) as "PendingAmount",
                   cast(coalesce(sum(e.commission_amount) filter (where e.status in ('APPROVED', 'ON_HOLD')), 0) as bigint) as "ApprovedAmount",
                   cast(coalesce(sum(e.commission_amount) filter (where e.status = 'PAID'), 0) as bigint) as "PaidAmount"
            {{CommissionEntrySql.FromAndWhere}}
             group by e.staff_id, p.code, p.name
             order by p.name, e.staff_id
             limit {9}
            """, parameters).ToListAsync(cancellationToken);

        return new CommissionSummaryReportResponse(
            rows.Take(rowLimit).Select(row => new CommissionSummaryRowResponse(
                row.StaffId, row.StaffCode, row.StaffName, row.BaseAmount, row.EarnedAmount, row.ReversedAmount, row.NetAmount,
                row.PendingAmount, row.ApprovedAmount, row.PaidAmount)).ToList(),
            totals.TotalBaseAmount, totals.TotalEarnedAmount, totals.TotalReversedAmount, totals.TotalNetAmount, totals.TotalPendingAmount,
            totals.TotalApprovedAmount, totals.TotalPaidAmount, totals.StaffCount, rows.Count > rowLimit);
    }

    public async Task<CommissionDetailReportResponse> GetDetailAsync(CommissionReportRequest request, int rowLimit, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var filterParameters = BuildFilterParameters(request);

        var totals = await databaseContext.Database.SqlQueryRaw<DetailTotalsRow>($$"""
            select cast(coalesce(sum(e.base_amount), 0) as bigint) as "TotalBaseAmount",
                   cast(coalesce(sum(e.commission_amount), 0) as bigint) as "TotalCommissionAmount",
                   count(*) as "EntryCount"
            {{CommissionEntrySql.FromAndWhere}}
            """, filterParameters).SingleAsync(cancellationToken);

        var parameters = filterParameters.Concat(new object?[] { rowLimit + 1 }).ToArray();
        var rows = await databaseContext.Database.SqlQueryRaw<CommissionEntrySql.EntryRow>($$"""
            select {{CommissionEntrySql.Columns}} {{CommissionEntrySql.FromAndWhere}}
             order by p.name, e.sale_date, i.invoice_no, e.id
             limit {9}
            """, parameters).ToListAsync(cancellationToken);

        return new CommissionDetailReportResponse(
            rows.Take(rowLimit).Select(CommissionEntrySql.ToResponse).ToList(), totals.TotalBaseAmount, totals.TotalCommissionAmount, totals.EntryCount, rows.Count > rowLimit);
    }

    private object?[] BuildFilterParameters(CommissionReportRequest request) =>
    [
        currentUser.CompanyId, request.StoreId, request.StaffId, null, request.FromDate, request.ToDate,
        currentUser.HasAccessToAllStores, currentUser.AssignedStoreIds.ToArray(), null,
    ];

    private sealed class SummaryTotalsRow
    {
        public long TotalBaseAmount { get; set; }
        public long TotalEarnedAmount { get; set; }
        public long TotalReversedAmount { get; set; }
        public long TotalNetAmount { get; set; }
        public long TotalPendingAmount { get; set; }
        public long TotalApprovedAmount { get; set; }
        public long TotalPaidAmount { get; set; }
        public long StaffCount { get; set; }
    }

    private sealed class SummaryRow
    {
        public Guid StaffId { get; set; }
        public string StaffCode { get; set; } = "";
        public string StaffName { get; set; } = "";
        public long BaseAmount { get; set; }
        public long EarnedAmount { get; set; }
        public long ReversedAmount { get; set; }
        public long NetAmount { get; set; }
        public long PendingAmount { get; set; }
        public long ApprovedAmount { get; set; }
        public long PaidAmount { get; set; }
    }

    private sealed class DetailTotalsRow
    {
        public long TotalBaseAmount { get; set; }
        public long TotalCommissionAmount { get; set; }
        public long EntryCount { get; set; }
    }
}
