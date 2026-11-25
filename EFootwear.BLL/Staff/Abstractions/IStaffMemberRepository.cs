using EFootwear.BLL.Common;
using EFootwear.BLL.Staff.Contracts;

namespace EFootwear.BLL.Staff.Abstractions;

/// <summary>
/// Data access for staff members and their store history. Implemented in EFootwear.DAL/Staff/Repositories/StaffMemberRepository.cs.
/// Soft-deleted staff are never returned.
/// </summary>
public interface IStaffMemberRepository
{
    Task<PagedResult<StaffMemberListItemResponse>> SearchAsync(StaffMemberSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken);
    Task<IReadOnlyList<StaffMemberLookupItemResponse>> LookupAsync(string? searchText, int maximumItemCount, CancellationToken cancellationToken);
    Task<StaffMemberDetailResponse?> FindDetailAsync(Guid staffId, CancellationToken cancellationToken);

    /// <summary>Inserts the staff member with the first store history row and saves, so later steps in the same transaction can refer to the id.</summary>
    Task<Guid> AddAsync(StaffMemberWriteRecord record, Guid storeId, string code, Guid createdBy, CancellationToken cancellationToken);

    Task UpdateAsync(Guid staffId, StaffMemberWriteRecord record, long rowVersion, CancellationToken cancellationToken);
    Task SoftDeleteAsync(Guid staffId, Guid deletedBy, CancellationToken cancellationToken);

    Task<Guid?> FindStoreIdAsync(Guid staffId, CancellationToken cancellationToken);
    Task TransferAsync(Guid staffId, Guid fromStoreId, Guid toStoreId, DateOnly effectiveOn, Guid changedBy, CancellationToken cancellationToken);
    Task<IReadOnlyList<StaffStoreHistoryResponse>> ListStoreHistoryAsync(Guid staffId, CancellationToken cancellationToken);
}
