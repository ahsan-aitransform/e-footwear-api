using System.Text.Json;
using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Core.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EFootwear.DAL.Core.Repositories;

/// <summary>
/// The company row. Reading uses EF Core. platform.companies is read-only for the application role, so writing calls
/// platform.update_company_profile, which checks the company, applies only whitelisted fields and writes the audit entry.
/// </summary>
public sealed class CompanyProfileRepository(DatabaseSession databaseSession) : ICompanyProfileRepository
{
    public async Task<CompanyProfileResponse?> FindAsync(Guid companyId, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        return await databaseContext.Companies.AsNoTracking()
            .Where(company => company.Id == companyId)
            .Select(company => new CompanyProfileResponse(company.Id, company.Name, company.LegalName, company.Ntn, company.Strn,
                company.Timezone, company.CurrencyCode.Trim(), company.RoundingMode, company.PriceBandBasis, company.InvoiceTerms,
                company.PlanCode, company.Status, company.RowVersion))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task UpdateAsync(Guid companyId, IReadOnlyDictionary<string, string?> patch, long? expectedRowVersion,
                                  CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var patchJson = JsonSerializer.Serialize(patch);
        try
        {
            await databaseContext.Database.ExecuteSqlAsync(
                $"select platform.update_company_profile({companyId}, {patchJson}::jsonb, {expectedRowVersion}::bigint)", cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            throw new DomainException(ErrorKind.Conflict, "The company profile was changed by someone else. Reload it and try again.",
                ErrorCodes.ConcurrentUpdate);
        }
    }
}
