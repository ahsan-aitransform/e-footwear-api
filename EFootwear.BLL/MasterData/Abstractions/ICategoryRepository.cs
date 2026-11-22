using EFootwear.BLL.Common;
using EFootwear.BLL.MasterData.Contracts;

namespace EFootwear.BLL.MasterData.Abstractions;

/// <summary>Data access for categories (master.categories). Implemented in EFootwear.DAL/MasterData/Repositories/CategoryRepository.cs.</summary>
public interface ICategoryRepository
{
    Task<PagedResult<CategoryResponse>> ListAsync(Guid? departmentId, PageRequest pageRequest, bool includeInactive, CancellationToken cancellationToken);
    Task<IReadOnlyList<MasterListOptionResponse>> ListActiveOptionsAsync(Guid? departmentId, CancellationToken cancellationToken);
    Task<CategoryResponse?> FindAsync(Guid categoryId, CancellationToken cancellationToken);
    Task<bool> NameExistsInDepartmentAsync(Guid departmentId, string name, Guid? exceptCategoryId, CancellationToken cancellationToken);

    /// <summary>The ids of every category above this one (parent, grandparent ...), used to refuse a loop.</summary>
    Task<IReadOnlyList<Guid>> ListAncestorIdsAsync(Guid categoryId, CancellationToken cancellationToken);

    Task<Guid> AddAsync(CreateCategoryRequest normalizedRequest, Guid createdBy, CancellationToken cancellationToken);
    Task UpdateAsync(Guid categoryId, UpdateCategoryRequest normalizedRequest, CancellationToken cancellationToken);
    Task SoftDeleteAsync(Guid categoryId, Guid deletedBy, CancellationToken cancellationToken);
}
