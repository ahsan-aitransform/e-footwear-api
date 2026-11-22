using EFootwear.BLL.Core.Contracts;

namespace EFootwear.BLL.Core.Abstractions;

/// <summary>Store administration by the company (core.stores). Implemented in EFootwear.DAL/Core/Repositories/StoreManagementRepository.cs.</summary>
public interface IStoreManagementRepository
{
    Task<IReadOnlyList<StoreDetailResponse>> ListAllAsync(Guid companyId, CancellationToken cancellationToken);

    Task<StoreDetailResponse?> FindAsync(Guid companyId, Guid storeId, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(Guid companyId, string code, CancellationToken cancellationToken);

    Task<Guid> CreateAsync(Guid companyId, string code, string name, string? address, string? phone, CancellationToken cancellationToken);

    Task UpdateAsync(Guid companyId, Guid storeId, string name, string? address, string? phone, long expectedRowVersion,
                     CancellationToken cancellationToken);

    Task SetActiveAsync(Guid companyId, Guid storeId, bool isActive, CancellationToken cancellationToken);
}
