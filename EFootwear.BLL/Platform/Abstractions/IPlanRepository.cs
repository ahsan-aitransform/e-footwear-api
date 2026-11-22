using EFootwear.BLL.Platform.Contracts;

namespace EFootwear.BLL.Platform.Abstractions;

/// <summary>Plans (platform.plans) and what a company uses of its plan. Plans are written through database functions.</summary>
public interface IPlanRepository
{
    Task<IReadOnlyList<PlanResponse>> ListAsync(CancellationToken cancellationToken);

    Task<PlanResponse?> FindAsync(string planCode, CancellationToken cancellationToken);

    Task CreateAsync(Guid administratorId, string code, string name, int maximumStores, int maximumUsers, CancellationToken cancellationToken);

    Task UpdateAsync(Guid administratorId, string code, string name, int maximumStores, int maximumUsers, bool isActive,
                     CancellationToken cancellationToken);

    Task<PlanUsageResponse?> GetUsageForCompanyAsync(Guid companyId, CancellationToken cancellationToken);
}
