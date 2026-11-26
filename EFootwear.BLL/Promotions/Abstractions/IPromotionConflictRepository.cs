using EFootwear.BLL.Common;
using EFootwear.BLL.Promotions.Contracts;

namespace EFootwear.BLL.Promotions.Abstractions;

/// <summary>
/// The open promotion conflicts (promo.conflict_groups) and their resolution. The engine opens a group when two or more promotions cover the same size in the same store
/// and sells the size at the normal price until a person chooses one; choosing sets the group to RESOLVED. Implemented in the DAL.
/// </summary>
public interface IPromotionConflictRepository
{
    Task<PagedResult<PromotionConflictResponse>> ListOpenAsync(Guid? storeId, PageRequest pageRequest, CancellationToken cancellationToken);

    /// <summary>The ids of the promotions of an open group, or null when there is no open group with that id.</summary>
    Task<IReadOnlyList<Guid>?> FindOpenGroupCandidateIdsAsync(Guid groupId, CancellationToken cancellationToken);

    /// <summary>Marks the group resolved with the chosen promotion. False means the group is no longer open.</summary>
    Task<bool> ResolveAsync(Guid groupId, Guid chosenRuleId, Guid userId, CancellationToken cancellationToken);
}
