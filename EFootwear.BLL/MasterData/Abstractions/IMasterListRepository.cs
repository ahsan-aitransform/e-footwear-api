using EFootwear.BLL.Common;
using EFootwear.BLL.MasterData.Contracts;
using EFootwear.Domain.MasterData;

namespace EFootwear.BLL.MasterData.Abstractions;

/// <summary>
/// Data access for the ten simple master lists. One implementation serves every list: the <see cref="MasterListDefinition"/>
/// says which table to use. Implemented in EFootwear.DAL/MasterData/Repositories/MasterListRepository.cs.
/// Soft-deleted rows are never returned. Updates carry the row version the client saw.
/// </summary>
public interface IMasterListRepository
{
    Task<PagedResult<MasterListItemResponse>> ListAsync(MasterListDefinition definition, PageRequest pageRequest, bool includeInactive,
                                                        CancellationToken cancellationToken);

    Task<IReadOnlyList<MasterListOptionResponse>> ListActiveOptionsAsync(MasterListDefinition definition, CancellationToken cancellationToken);
    Task<MasterListItemResponse?> FindAsync(MasterListDefinition definition, Guid itemId, CancellationToken cancellationToken);
    Task<bool> NameExistsAsync(MasterListDefinition definition, string name, Guid? exceptItemId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListNamesAsync(MasterListDefinition definition, CancellationToken cancellationToken);
    Task<Guid> AddAsync(MasterListDefinition definition, CreateMasterListItemRequest request, Guid createdBy, CancellationToken cancellationToken);
    Task UpdateAsync(MasterListDefinition definition, Guid itemId, UpdateMasterListItemRequest request, CancellationToken cancellationToken);
    Task SoftDeleteAsync(MasterListDefinition definition, Guid itemId, Guid deletedBy, CancellationToken cancellationToken);
}
