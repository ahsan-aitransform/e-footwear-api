using EFootwear.BLL.Common;
using EFootwear.BLL.Parties.Contracts;

namespace EFootwear.BLL.Parties.Abstractions;

/// <summary>
/// Data access for parties, their staff job details and staff store history. Implemented in
/// EFootwear.DAL/Parties/Repositories/PartyRepository.cs. Soft-deleted parties are never returned.
/// </summary>
public interface IPartyRepository
{
    Task<PagedResult<PartyListItemResponse>> SearchAsync(PartySearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken);
    Task<IReadOnlyList<PartyLookupItemResponse>> LookupAsync(string partyTypeCode, string? searchText, int maximumItemCount, CancellationToken cancellationToken);
    Task<PartyDetailResponse?> FindDetailAsync(Guid partyId, CancellationToken cancellationToken);
    Task<bool> CnicExistsAsync(string cnic, Guid? exceptPartyId, CancellationToken cancellationToken);

    /// <summary>Allocates the next party code (group 103 plus a serial) from the database.</summary>
    Task<string> GenerateNextCodeAsync(CancellationToken cancellationToken);

    /// <summary>Inserts the party (and its staff profile) and saves, so later steps in the same transaction can refer to its id.</summary>
    Task<Guid> AddAsync(PartyWriteRecord record, string code, Guid createdBy, CancellationToken cancellationToken);

    Task UpdateAsync(Guid partyId, PartyWriteRecord record, long rowVersion, CancellationToken cancellationToken);
    Task SoftDeleteAsync(Guid partyId, Guid deletedBy, CancellationToken cancellationToken);

    Task<Guid?> FindStaffStoreIdAsync(Guid partyId, CancellationToken cancellationToken);
    Task TransferStaffAsync(Guid partyId, Guid fromStoreId, Guid toStoreId, DateOnly effectiveOn, Guid changedBy, CancellationToken cancellationToken);
    Task<IReadOnlyList<StaffStoreHistoryResponse>> ListStaffStoreHistoryAsync(Guid partyId, CancellationToken cancellationToken);
}
