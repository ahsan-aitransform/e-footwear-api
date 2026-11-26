using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Commissions.Abstractions;
using EFootwear.BLL.Commissions.Contracts;
using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Services;
using EFootwear.Domain.Commissions;
using EFootwear.Domain.Common;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Commissions.Services;

/// <summary>
/// The commission entries and their approval (Part 16.2). Entries are made by the database when a sale is posted and reversed when it is returned or voided;
/// the unique source reference of every entry (invoice line, staff member and plan) is what stops one sale from being counted twice. Approving turns the
/// pending entries of a store and a range of dates into approved ones; the salary sheet of the month then takes approved commission, and the entry becomes
/// paid when that salary line is paid. Nothing is paid from here.
/// </summary>
public sealed class CommissionEntryService(
    ICommissionEntryRepository commissionEntryRepository,
    StoreAccessGuard storeAccessGuard,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<CommissionEntryService> logger)
{
    public async Task<PagedResult<CommissionEntryResponse>> SearchAsync(CommissionEntrySearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        await EnsureFilterIsValidAsync(filter, cancellationToken);
        return await commissionEntryRepository.SearchAsync(filter, pageRequest.Normalized(), cancellationToken);
    }

    public async Task<CommissionEntryTotalsResponse> GetTotalsAsync(CommissionEntrySearchRequest filter, CancellationToken cancellationToken)
    {
        await EnsureFilterIsValidAsync(filter, cancellationToken);
        return await commissionEntryRepository.GetTotalsAsync(filter, cancellationToken);
    }

    public async Task<ApproveCommissionsResponse> ApproveAsync(ApproveCommissionsRequest request, CancellationToken cancellationToken)
    {
        await storeAccessGuard.EnsureAccessibleAsync(request.StoreId, cancellationToken);
        CommissionRules.EnsureDateRangeIsValid(request.FromDate, request.ToDate);

        var approvedEntryCount = await commissionEntryRepository.ApproveAsync(request.StoreId, request.FromDate, request.ToDate, currentUser.UserId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("{ApprovedEntryCount} commission entries of store {StoreId} from {FromDate} to {ToDate} approved by user {UserId}",
            approvedEntryCount, request.StoreId, request.FromDate, request.ToDate, currentUser.UserId);
        return new ApproveCommissionsResponse(approvedEntryCount);
    }

    private async Task EnsureFilterIsValidAsync(CommissionEntrySearchRequest filter, CancellationToken cancellationToken)
    {
        if (filter.StoreId is { } storeId) await storeAccessGuard.EnsureAccessibleAsync(storeId, cancellationToken);
        if (filter.StatusCode is not null && !CommissionEntryStatusCodes.IsValid(filter.StatusCode))
            throw new DomainException(ErrorKind.Validation, "Choose a valid commission status.");
        if (filter.FromDate is { } fromDate && filter.ToDate is { } toDate) CommissionRules.EnsureDateRangeIsValid(fromDate, toDate);
    }
}
