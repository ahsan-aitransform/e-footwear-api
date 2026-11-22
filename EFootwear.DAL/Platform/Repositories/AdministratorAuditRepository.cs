using System.Text.Json;
using EFootwear.BLL.Common;
using EFootwear.BLL.Platform.Abstractions;
using EFootwear.BLL.Platform.Contracts;
using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Platform.Repositories;

/// <summary>platform.admin_actions joined with the administrator and the company, newest first, with optional filters.</summary>
public sealed class AdministratorAuditRepository(DatabaseSession databaseSession) : IAdministratorAuditRepository
{
    public async Task<PagedResult<AdministratorAuditEntryResponse>> ListAsync(
        AdministratorAuditQuery query, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var from = query.From?.ToUniversalTime();
        var to = query.To?.ToUniversalTime();

        var totalCount = (await databaseContext.Database.SqlQuery<long>($"""
            select count(*) as "Value"
              from platform.admin_actions a
             where ({query.CompanyId}::uuid is null or a.company_id = {query.CompanyId}::uuid)
               and ({query.ActionCode}::text is null or a.action = {query.ActionCode}::text)
               and ({from}::timestamptz is null or a.occurred_at >= {from}::timestamptz)
               and ({to}::timestamptz is null or a.occurred_at <= {to}::timestamptz)
            """).ToListAsync(cancellationToken)).Single();

        var skippedItemCount = pageRequest.SkippedItemCount;
        var pageSize = pageRequest.PageSize;
        var rows = await databaseContext.Database.SqlQuery<AuditRow>($"""
            select a.id as "Id", administrator.username as "AdministratorUsername", a.action as "ActionCode",
                   a.company_id as "CompanyId", company.name as "CompanyName", a.detail::text as "DetailJson",
                   a.occurred_at as "OccurredAt"
              from platform.admin_actions a
              join platform.admins administrator on administrator.id = a.admin_id
              left join platform.companies company on company.id = a.company_id
             where ({query.CompanyId}::uuid is null or a.company_id = {query.CompanyId}::uuid)
               and ({query.ActionCode}::text is null or a.action = {query.ActionCode}::text)
               and ({from}::timestamptz is null or a.occurred_at >= {from}::timestamptz)
               and ({to}::timestamptz is null or a.occurred_at <= {to}::timestamptz)
             order by a.occurred_at desc, a.id desc
             offset {skippedItemCount} limit {pageSize}
            """).ToListAsync(cancellationToken);

        var items = rows.Select(row =>
        {
            using var detail = JsonDocument.Parse(row.DetailJson);
            return new AdministratorAuditEntryResponse(row.Id, row.AdministratorUsername, row.ActionCode, row.CompanyId,
                                                       row.CompanyName, detail.RootElement.Clone(), row.OccurredAt);
        }).ToList();
        return new PagedResult<AdministratorAuditEntryResponse>(items, pageRequest.PageNumber, pageRequest.PageSize, totalCount);
    }

    private sealed class AuditRow
    {
        public Guid Id { get; set; }
        public string AdministratorUsername { get; set; } = "";
        public string ActionCode { get; set; } = "";
        public Guid? CompanyId { get; set; }
        public string? CompanyName { get; set; }
        public string DetailJson { get; set; } = "{}";
        public DateTimeOffset OccurredAt { get; set; }
    }
}
