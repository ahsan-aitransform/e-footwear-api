using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Parties.Abstractions;
using EFootwear.BLL.Parties.Contracts;
using EFootwear.Domain.Common;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Parties.Services;

/// <summary>
/// Moves a staff member to another store of the same company (docx 3.1 improvement, Part 16.1). The person's current store is
/// updated and a history row records the previous store, the new store, the effective date and who did it; the history is only
/// ever added to. The caller must be allowed to act in both stores.
/// </summary>
public sealed class StaffTransferService(
    IPartyRepository partyRepository,
    IStoreRepository storeRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<StaffTransferService> logger)
{
    public async Task TransferAsync(Guid partyId, TransferStaffRequest request, CancellationToken cancellationToken)
    {
        _ = await partyRepository.FindDetailAsync(partyId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "The party was not found.");
        var fromStoreId = await partyRepository.FindStaffStoreIdAsync(partyId, cancellationToken)
            ?? throw new DomainException(ErrorKind.Validation, "This party is not a staff member.");

        if (request.ToStoreId == fromStoreId)
            throw new DomainException(ErrorKind.Validation, "The staff member already works in this store.");
        if (!currentUser.CanAccessStore(fromStoreId) || !currentUser.CanAccessStore(request.ToStoreId))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to both stores.");
        var validStoreIds = await storeRepository.ActiveSellingStoreIdsAsync(currentUser.CompanyId, [request.ToStoreId], cancellationToken);
        if (validStoreIds.Count != 1)
            throw new DomainException(ErrorKind.Validation, "Choose an active store of this company.");

        await partyRepository.TransferStaffAsync(partyId, fromStoreId, request.ToStoreId, request.EffectiveOn, currentUser.UserId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Staff {PartyId} moved from store {FromStoreId} to {ToStoreId} by user {UserId}", partyId, fromStoreId, request.ToStoreId, currentUser.UserId);
    }

    public async Task<IReadOnlyList<StaffStoreHistoryResponse>> ListHistoryAsync(Guid partyId, CancellationToken cancellationToken)
    {
        _ = await partyRepository.FindDetailAsync(partyId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "The party was not found.");
        return await partyRepository.ListStaffStoreHistoryAsync(partyId, cancellationToken);
    }
}
