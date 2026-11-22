using System.Text.Json;
using EFootwear.BLL.Platform.Abstractions;
using EFootwear.BLL.Platform.Contracts;
using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Platform.Repositories;

/// <summary>
/// DAL for companies and the administrator audit trail (EF Core). Runs in the request's DatabaseSession, where the
/// platform-administrator flag is set, so row-level security lets the caller read every company.
/// </summary>
public sealed class PlatformRepository(DatabaseSession databaseSession) : IPlatformRepository
{
    public async Task<IReadOnlyList<CompanySummaryResponse>> ListCompaniesAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        return await databaseContext.Companies.AsNoTracking()
            .OrderBy(company => company.Id)
            .Select(company => new CompanySummaryResponse(company.Id, company.Name, company.Status, company.Timezone,
                                                          company.CurrencyCode.Trim(), company.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> CompanyExistsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        return await databaseContext.Companies.AnyAsync(company => company.Id == companyId, cancellationToken);
    }

    // The company, its first store and its owner are created together by one database function.
    public async Task<CreateCompanyResponse> CreateCompanyAsync(Guid administratorId, CreateCompanyRequest request,
                                                                string ownerPasswordHash, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var row = await databaseContext.Database.SqlQuery<CreatedCompanyRow>($"""
            select out_company_id as "CompanyId", out_store_id as "FirstStoreId", out_owner_id as "OwnerUserId"
              from platform.create_company({administratorId}, {request.Name}, {request.Timezone}, {request.OwnerEmail},
                                           {request.OwnerFullName}, {ownerPasswordHash},
                                           {request.FirstStoreCode}, {request.FirstStoreName})
            """).SingleAsync(cancellationToken);
        return new CreateCompanyResponse(row.CompanyId, row.FirstStoreId, row.OwnerUserId);
    }

    public async Task LogAdministratorActionAsync(Guid administratorId, string actionCode, Guid? companyId, object detail,
                                                  CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var detailJson = JsonSerializer.Serialize(detail);
        await databaseContext.Database.ExecuteSqlAsync($"""
            insert into platform.admin_actions (admin_id, action, company_id, detail)
            values ({administratorId}, {actionCode}, {companyId}, {detailJson}::jsonb)
            """, cancellationToken);
    }

    private sealed class CreatedCompanyRow
    {
        public Guid CompanyId { get; set; }
        public Guid FirstStoreId { get; set; }
        public Guid OwnerUserId { get; set; }
    }
}
