using EFootwear.BLL.Platform.Abstractions;
using EFootwear.BLL.Platform.Contracts;
using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Platform.Repositories;

/// <summary>
/// Plans and plan usage. Plans are read with EF Core and written by the database functions platform.create_plan / update_plan
/// (the application role has no INSERT or UPDATE on platform.plans). Usage counts the company's active stores (head office excluded)
/// and users (deleted users excluded), under the company's row-level security.
/// </summary>
public sealed class PlanRepository(DatabaseSession databaseSession) : IPlanRepository
{
    public async Task<IReadOnlyList<PlanResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        return await databaseContext.Plans.AsNoTracking()
            .OrderBy(plan => plan.Code)
            .Select(plan => new PlanResponse(plan.Code, plan.Name, plan.MaxStores, plan.MaxUsers, plan.IsActive,
                databaseContext.Companies.Count(company => company.PlanCode == plan.Code)))
            .ToListAsync(cancellationToken);
    }

    public async Task<PlanResponse?> FindAsync(string planCode, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        return await databaseContext.Plans.AsNoTracking()
            .Where(plan => plan.Code == planCode)
            .Select(plan => new PlanResponse(plan.Code, plan.Name, plan.MaxStores, plan.MaxUsers, plan.IsActive,
                databaseContext.Companies.Count(company => company.PlanCode == plan.Code)))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task CreateAsync(Guid administratorId, string code, string name, int maximumStores, int maximumUsers,
                                  CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        await databaseContext.Database.ExecuteSqlAsync($"""
            select platform.create_plan({administratorId}, {code}, {name}, {maximumStores}, {maximumUsers}, null::jsonb)
            """, cancellationToken);
    }

    public async Task UpdateAsync(Guid administratorId, string code, string name, int maximumStores, int maximumUsers, bool isActive,
                                  CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        await databaseContext.Database.ExecuteSqlAsync($"""
            select platform.update_plan({administratorId}, {code}, {name}, {maximumStores}, {maximumUsers}, null::jsonb, {isActive})
            """, cancellationToken);
    }

    public async Task<PlanUsageResponse?> GetUsageForCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var rows = await databaseContext.Database.SqlQuery<UsageRow>($"""
            select p.code as "PlanCode", p.name as "PlanName", p.max_stores as "MaximumStores", p.max_users as "MaximumUsers",
                   (select count(*) from core.stores s
                     where s.company_id = c.id and not s.is_head_office and s.is_active)::int as "ActiveStores",
                   (select count(*) from sec.users u where u.company_id = c.id and not u.is_deleted)::int as "Users"
              from platform.companies c
              join platform.plans p on p.code = c.plan_code
             where c.id = {companyId}
            """).ToListAsync(cancellationToken);
        return rows.Select(row => new PlanUsageResponse(row.PlanCode, row.PlanName, row.MaximumStores, row.MaximumUsers,
                                                         row.ActiveStores, row.Users)).FirstOrDefault();
    }

    private sealed class UsageRow
    {
        public string PlanCode { get; set; } = "";
        public string PlanName { get; set; } = "";
        public int MaximumStores { get; set; }
        public int MaximumUsers { get; set; }
        public int ActiveStores { get; set; }
        public int Users { get; set; }
    }
}
