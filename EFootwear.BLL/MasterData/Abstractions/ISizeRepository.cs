using EFootwear.BLL.MasterData.Contracts;

namespace EFootwear.BLL.MasterData.Abstractions;

/// <summary>Data access for the sizes of a size group (master.sizes). Implemented in EFootwear.DAL/MasterData/Repositories/SizeRepository.cs.</summary>
public interface ISizeRepository
{
    Task<IReadOnlyList<SizeResponse>> ListBySizeGroupAsync(Guid sizeGroupId, CancellationToken cancellationToken);
    Task<SizeResponse?> FindAsync(Guid sizeGroupId, Guid sizeId, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(Guid sizeGroupId, string sizeCode, Guid? exceptSizeId, CancellationToken cancellationToken);
    Task<Guid> AddAsync(Guid sizeGroupId, string sizeCode, int sortOrder, CancellationToken cancellationToken);
    Task UpdateAsync(Guid sizeGroupId, Guid sizeId, string sizeCode, int sortOrder, CancellationToken cancellationToken);
    Task DeleteAsync(Guid sizeGroupId, Guid sizeId, CancellationToken cancellationToken);
}
