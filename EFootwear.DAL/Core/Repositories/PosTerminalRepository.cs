using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Core.Contracts;
using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Core.Repositories;

/// <summary>Reads core.devices. Row-level security limits the rows to the stores the caller may work in.</summary>
public sealed class PosTerminalRepository(DatabaseSession databaseSession) : IPosTerminalRepository
{
    public async Task<IReadOnlyList<PosTerminalResponse>> ListAsync(Guid companyId, Guid? storeId, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var rows = await databaseContext.Database.SqlQuery<TerminalRow>($"""
            select d.id as "Id", d.store_id as "StoreId", s.code as "StoreCode", d.label as "Label",
                   d.is_active as "IsActive", d.last_seen_at as "LastSeenAt"
              from core.devices d
              join core.stores s on s.company_id = d.company_id and s.id = d.store_id
             where d.company_id = {companyId} and ({storeId}::uuid is null or d.store_id = {storeId}::uuid)
            """).ToListAsync(cancellationToken);
        return rows.OrderBy(row => row.StoreCode).ThenBy(row => row.Label).ThenBy(row => row.Id)
                   .Select(row => new PosTerminalResponse(row.Id, row.StoreId, row.StoreCode, row.Label, row.IsActive, row.LastSeenAt))
                   .ToList();
    }

    private sealed class TerminalRow
    {
        public Guid Id { get; set; }
        public Guid StoreId { get; set; }
        public string StoreCode { get; set; } = "";
        public string? Label { get; set; }
        public bool IsActive { get; set; }
        public DateTimeOffset? LastSeenAt { get; set; }
    }
}
