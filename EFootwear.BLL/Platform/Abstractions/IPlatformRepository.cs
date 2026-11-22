using EFootwear.BLL.Platform.Contracts;

namespace EFootwear.BLL.Platform.Abstractions;

/// <summary>Platform-level data access: companies and the administrator audit trail.</summary>
public interface IPlatformRepository
{
    Task<IReadOnlyList<CompanySummaryResponse>> ListCompaniesAsync(CancellationToken cancellationToken);

    Task<bool> CompanyExistsAsync(Guid companyId, CancellationToken cancellationToken);

    /// <summary>Creates the company, its first store and its owner in one database function.</summary>
    Task<CreateCompanyResponse> CreateCompanyAsync(Guid administratorId, CreateCompanyRequest request,
                                                   string ownerPasswordHash, CancellationToken cancellationToken);

    Task LogAdministratorActionAsync(Guid administratorId, string actionCode, Guid? companyId, object detail,
                                     CancellationToken cancellationToken);
}
