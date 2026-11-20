using System.Text.Json;
using EFootwear.BLL.Platform;
using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Platform;

/// <summary>
/// DAL for companies and the administrator audit trail (EF Core). Runs in the request's DbSession, where the
/// platform-administrator flag is set, so row-level security lets the caller read every company.
/// </summary>
public sealed class PlatformRepository(DbSession db) : IPlatformRepository
{
    public async Task<IReadOnlyList<CompanySummary>> ListCompaniesAsync(CancellationToken ct)
    {
        var ctx = await db.DbAsync(ct);
        return await ctx.Companies.AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => new CompanySummary(c.Id, c.Name, c.Status, c.Timezone, c.CurrencyCode.Trim(), c.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<bool> CompanyExistsAsync(long companyId, CancellationToken ct)
    {
        var ctx = await db.DbAsync(ct);
        return await ctx.Companies.AnyAsync(c => c.Id == companyId, ct);
    }

    // The company, its first store and its owner are created together by one database function.
    public async Task<CreatedCompany> CreateCompanyAsync(long adminId, CreateCompanyRequest request, string ownerPasswordHash, CancellationToken ct)
    {
        var ctx = await db.DbAsync(ct);
        var row = await ctx.Database.SqlQuery<CreatedRow>($"""
            select out_company_id as "CompanyId", out_store_id as "FirstStoreId", out_owner_id as "OwnerUserId"
              from platform.create_company({adminId}, {request.Name}, {request.Timezone}, {request.OwnerEmail},
                                           {request.OwnerFullName}, {ownerPasswordHash},
                                           {request.FirstStoreCode}, {request.FirstStoreName})
            """).SingleAsync(ct);
        return new CreatedCompany(row.CompanyId, row.FirstStoreId, row.OwnerUserId);
    }

    public async Task LogAdminActionAsync(long adminId, string action, long? companyId, object detail, CancellationToken ct)
    {
        var ctx = await db.DbAsync(ct);
        var json = JsonSerializer.Serialize(detail);
        await ctx.Database.ExecuteSqlAsync($"""
            insert into platform.admin_actions (admin_id, action, company_id, detail)
            values ({adminId}, {action}, {companyId}, {json}::jsonb)
            """, ct);
    }

    private sealed class CreatedRow
    {
        public long CompanyId { get; set; }
        public long FirstStoreId { get; set; }
        public long OwnerUserId { get; set; }
    }
}
