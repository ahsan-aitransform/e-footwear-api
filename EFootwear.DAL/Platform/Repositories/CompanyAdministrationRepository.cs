using EFootwear.BLL.Platform.Abstractions;
using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Platform.Repositories;

/// <summary>Plan assignment and company status. Both are database functions that re-check that the administrator is active.</summary>
public sealed class CompanyAdministrationRepository(DatabaseSession databaseSession) : ICompanyAdministrationRepository
{
    public async Task AssignPlanAsync(Guid administratorId, Guid companyId, string planCode, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        await databaseContext.Database.ExecuteSqlAsync(
            $"select platform.assign_company_plan({administratorId}, {companyId}, {planCode})", cancellationToken);
    }

    public async Task SetStatusAsync(Guid administratorId, Guid companyId, string status, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        await databaseContext.Database.ExecuteSqlAsync(
            $"select platform.set_company_status({administratorId}, {companyId}, {status})", cancellationToken);
    }
}
