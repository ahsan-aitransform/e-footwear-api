using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Staff.Abstractions;
using EFootwear.BLL.Staff.Contracts;
using EFootwear.Domain.Common;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Staff.Services;

/// <summary>
/// Moves a staff member to another store of the same company (Part 16.1). The person's current store is updated and a history row records the
/// previous store, the new store, the effective date and who did it; the history is only ever added to. The caller must be allowed to act in both stores.
/// </summary>
public sealed class StaffTransferService(
    IStaffMemberRepository staffRepository,
    IStoreRepository storeRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<StaffTransferService> logger)
{
    public async Task TransferAsync(Guid staffId, TransferStaffRequest request, CancellationToken cancellationToken)
    {
        _ = await staffRepository.FindDetailAsync(staffId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "The staff member was not found.");
        var fromStoreId = await staffRepository.FindStoreIdAsync(staffId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "The staff member was not found.");

        if (request.ToStoreId == fromStoreId)
            throw new DomainException(ErrorKind.Validation, "The staff member already works in this store.");
        if (!currentUser.CanAccessStore(fromStoreId) || !currentUser.CanAccessStore(request.ToStoreId))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to both stores.");
        var validStoreIds = await storeRepository.ActiveSellingStoreIdsAsync(currentUser.CompanyId, [request.ToStoreId], cancellationToken);
        if (validStoreIds.Count != 1)
            throw new DomainException(ErrorKind.Validation, "Choose an active store of this company.");

        await staffRepository.TransferAsync(staffId, fromStoreId, request.ToStoreId, request.EffectiveOn, currentUser.UserId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Staff {StaffId} moved from store {FromStoreId} to {ToStoreId} by user {UserId}", staffId, fromStoreId, request.ToStoreId, currentUser.UserId);
    }

    public async Task<IReadOnlyList<StaffStoreHistoryResponse>> ListHistoryAsync(Guid staffId, CancellationToken cancellationToken)
    {
        _ = await staffRepository.FindDetailAsync(staffId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "The staff member was not found.");
        return await staffRepository.ListStoreHistoryAsync(staffId, cancellationToken);
    }
}
