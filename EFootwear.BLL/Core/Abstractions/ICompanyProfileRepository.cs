using EFootwear.BLL.Core.Contracts;

namespace EFootwear.BLL.Core.Abstractions;

/// <summary>
/// The company row (platform.companies). It is read-only for the application role, so a change goes through the database
/// function platform.update_company_profile, which also checks that a company changes only itself and writes the audit entry.
/// </summary>
public interface ICompanyProfileRepository
{
    Task<CompanyProfileResponse?> FindAsync(Guid companyId, CancellationToken cancellationToken);

    /// <summary>Keys are column names (name, legal_name, ntn, strn, timezone, currency_code, rounding_mode, price_band_basis, invoice_terms).</summary>
    Task UpdateAsync(Guid companyId, IReadOnlyDictionary<string, string?> patch, long? expectedRowVersion,
                     CancellationToken cancellationToken);
}
