using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Commissions.Abstractions;
using EFootwear.BLL.Commissions.Contracts;
using EFootwear.BLL.Common;
using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Commissions.Repositories;

/// <summary>
/// The commission entries: listing, totals and approval. Approval calls hr.approve_commissions, which checks the COMMISSION_APPROVE permission again inside
/// the database. Entries are never inserted, changed or deleted from here. The tables are isolated by company and store; the queries also name the caller's stores.
/// </summary>
public sealed class CommissionEntryRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : ICommissionEntryRepository
{
    private static readonly IReadOnlyDictionary<string, string> SortableColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["saleDate"] = "e.sale_date",
        ["staffName"] = "p.name",
        ["invoiceNumber"] = "i.invoice_no",
        ["commissionAmount"] = "e.commission_amount",
        ["baseAmount"] = "e.base_amount",
        ["statusCode"] = "e.status",
    };

    public async Task<PagedResult<CommissionEntryResponse>> SearchAsync(CommissionEntrySearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var filterParameters = BuildFilterParameters(filter, pageRequest.SearchText is null ? null : EFootwear.DAL.Common.LikePattern.Contains(pageRequest.SearchText));

        var totalCount = await databaseContext.Database
            .SqlQueryRaw<long>($"select count(*) as \"Value\" {CommissionEntrySql.FromAndWhere}", filterParameters)
            .SingleAsync(cancellationToken);
        if (totalCount == 0) return PagedResult<CommissionEntryResponse>.Empty(pageRequest);

        var direction = pageRequest.SortDirection == SortDirection.Descending ? "desc" : "asc";
        var primaryOrder = pageRequest.SortField is { } sortField && SortableColumns.TryGetValue(sortField, out var column) ? $"{column} {direction}, " : "";
        var parameters = filterParameters.Concat(new object?[] { pageRequest.PageSize, pageRequest.SkippedItemCount }).ToArray();

        var rows = await databaseContext.Database.SqlQueryRaw<CommissionEntrySql.EntryRow>($$"""
            select {{CommissionEntrySql.Columns}} {{CommissionEntrySql.FromAndWhere}}
             order by {{primaryOrder}}e.sale_date desc, i.invoice_no desc, p.name, e.id
             limit {9} offset {10}
            """, parameters).ToListAsync(cancellationToken);
        return new PagedResult<CommissionEntryResponse>(rows.Select(CommissionEntrySql.ToResponse).ToList(), pageRequest.PageNumber, pageRequest.PageSize, totalCount);
    }

    public async Task<CommissionEntryTotalsResponse> GetTotalsAsync(CommissionEntrySearchRequest filter, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var totals = await databaseContext.Database.SqlQueryRaw<TotalsRow>($$"""
            select cast(coalesce(sum(e.commission_amount) filter (where e.status = 'PENDING'), 0) as bigint) as "PendingAmount",
                   cast(coalesce(sum(e.commission_amount) filter (where e.status in ('APPROVED', 'ON_HOLD')), 0) as bigint) as "ApprovedAmount",
                   cast(coalesce(sum(e.commission_amount) filter (where e.status = 'PAID'), 0) as bigint) as "PaidAmount",
                   cast(coalesce(sum(e.commission_amount), 0) as bigint) as "TotalAmount",
                   count(*) as "EntryCount"
            {{CommissionEntrySql.FromAndWhere}}
            """, BuildFilterParameters(filter, null)).SingleAsync(cancellationToken);
        return new CommissionEntryTotalsResponse(totals.PendingAmount, totals.ApprovedAmount, totals.PaidAmount, totals.TotalAmount, totals.EntryCount);
    }

    public async Task<int> ApproveAsync(Guid storeId, DateOnly fromDate, DateOnly toDate, Guid userId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.Database.SqlQuery<int>($"""
            select hr.approve_commissions({companyId}, {storeId}, {fromDate}, {toDate}, {userId}) as "Value"
            """).SingleAsync(cancellationToken);
    }

    private object?[] BuildFilterParameters(CommissionEntrySearchRequest filter, string? searchPattern) =>
    [
        currentUser.CompanyId, filter.StoreId, filter.StaffId, filter.StatusCode, filter.FromDate, filter.ToDate,
        currentUser.HasAccessToAllStores, currentUser.AssignedStoreIds.ToArray(), searchPattern,
    ];

    private sealed class TotalsRow
    {
        public long PendingAmount { get; set; }
        public long ApprovedAmount { get; set; }
        public long PaidAmount { get; set; }
        public long TotalAmount { get; set; }
        public long EntryCount { get; set; }
    }
}
