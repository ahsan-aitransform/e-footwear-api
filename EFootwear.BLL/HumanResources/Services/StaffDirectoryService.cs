using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Services;
using EFootwear.BLL.HumanResources.Abstractions;
using EFootwear.BLL.HumanResources.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.HumanResources;

namespace EFootwear.BLL.HumanResources.Services;

/// <summary>
/// Reads staff: the staff list, one staff member with the salary history and the store history, and the company-wide list of staff transfers
/// (Part 16.1). A person who works in several stores sees only the staff of the stores they may use; row-level security enforces the same in the database.
/// </summary>
public sealed class StaffDirectoryService(IStaffRepository staffRepository, StoreAccessGuard storeAccessGuard, ICurrentUser currentUser)
{
    public async Task<PagedResult<StaffListItemResponse>> SearchAsync(StaffSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var normalizedPageRequest = pageRequest.Normalized();
        EnsureSearchTextIsNotTooLong(normalizedPageRequest);
        if (filter.StoreId is { } storeId) await storeAccessGuard.EnsureAccessibleAsync(storeId, cancellationToken);
        return await staffRepository.SearchStaffAsync(filter, normalizedPageRequest, cancellationToken);
    }

    public async Task<StaffDetailResponse> GetAsync(Guid staffId, CancellationToken cancellationToken)
    {
        var detail = await staffRepository.FindStaffDetailAsync(staffId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "The staff member was not found.");
        if (!currentUser.CanAccessStore(detail.Profile.CurrentStoreId))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to the store of this staff member.");
        return detail;
    }

    public async Task<PagedResult<StaffTransferListItemResponse>> SearchTransfersAsync(
        StaffTransferSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (filter.FromDate is { } fromDate && filter.ToDate is { } toDate && fromDate > toDate)
            throw new DomainException(ErrorKind.Validation, "The first date must not be after the last date.");
        if (filter.StoreId is { } storeId) await storeAccessGuard.EnsureAccessibleAsync(storeId, cancellationToken);
        var normalizedPageRequest = pageRequest.Normalized();
        EnsureSearchTextIsNotTooLong(normalizedPageRequest);
        return await staffRepository.SearchTransfersAsync(filter, normalizedPageRequest, cancellationToken);
    }

    private static void EnsureSearchTextIsNotTooLong(PageRequest pageRequest)
    {
        if (pageRequest.SearchText is { Length: > StaffRules.StaffSearchTextMaximumLength })
            throw new DomainException(ErrorKind.Validation, $"The search text can be at most {StaffRules.StaffSearchTextMaximumLength} characters.");
    }
}
