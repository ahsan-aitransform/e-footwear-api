using EFootwear.BLL.Abstractions;
using EFootwear.BLL.CashManagement.Abstractions;
using EFootwear.BLL.CashManagement.Contracts;
using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Abstractions;
using EFootwear.Domain.CashManagement;
using EFootwear.Domain.Common;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.CashManagement.Services;

/// <summary>
/// Use cases of the Promise screen (docx 3.5). A promise belongs to a store and to an active customer; the amount is whole rupees
/// above zero and may not be lowered below what was already paid; the due date cannot be before the day the promise was taken.
/// The paid amount is not edited here. "Delete" cancels the promise (it stays in the history). Which promises count as broken, and
/// what happens then, is not documented and is not decided here ([BUSINESS DECISION REQUIRED]).
/// </summary>
public sealed class PromiseService(
    IPromiseRepository promiseRepository,
    IStoreRepository storeRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<PromiseService> logger)
{
    public Task<PromiseListResponse> SearchAsync(PromiseSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (filter.StoreId is { } storeId) EnsureStoreAccess(storeId);
        if (filter.From is { } from && filter.To is { } to && from > to)
            throw new DomainException(ErrorKind.Validation, "The From date cannot be after the To date.");
        return promiseRepository.SearchAsync(filter, pageRequest.Normalized(), cancellationToken);
    }

    public async Task<PromiseResponse> GetAsync(Guid promiseId, CancellationToken cancellationToken) =>
        await promiseRepository.FindAsync(promiseId, cancellationToken)
        ?? throw new DomainException(ErrorKind.NotFound, "The promise was not found.");

    public async Task<CustomerBalanceResponse> GetCustomerBalanceAsync(Guid customerId, CancellationToken cancellationToken) =>
        await promiseRepository.GetCustomerBalanceAsync(customerId, cancellationToken)
        ?? throw new DomainException(ErrorKind.NotFound, "The customer was not found.");

    public async Task<CreatedIdResponse> CreateAsync(CreatePromiseRequest request, CancellationToken cancellationToken)
    {
        EnsureStoreAccess(request.StoreId);
        await EnsureStoreIsUsableAsync(request.StoreId, cancellationToken);
        if (!await promiseRepository.IsActiveCustomerAsync(request.CustomerId, cancellationToken))
            throw new DomainException(ErrorKind.Validation, "Choose an active customer.");
        PromiseRules.EnsureAmountAndDates(request.PromisedAmount, request.PromiseDate, request.DueDate);

        var normalized = request with { Note = PromiseRules.NormalizeNote(request.Note) };
        var promiseId = await promiseRepository.AddAsync(normalized, currentUser.UserId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Promise {PromiseId} created in company {CompanyId} by user {UserId}", promiseId, currentUser.CompanyId, currentUser.UserId);
        return new CreatedIdResponse(promiseId);
    }

    public async Task UpdateAsync(Guid promiseId, UpdatePromiseRequest request, CancellationToken cancellationToken)
    {
        var promise = await GetAsync(promiseId, cancellationToken);
        EnsureStoreAccess(promise.StoreId);
        if (!PromiseStatusCodes.IsStillActive(promise.StatusCode))
            throw new DomainException(ErrorKind.Conflict, "Only an open or partly paid promise can be changed.");
        PromiseRules.EnsureAmountAndDates(request.PromisedAmount, promise.PromiseDate, request.DueDate);
        if (request.PromisedAmount < promise.PaidAmount)
            throw new DomainException(ErrorKind.Validation, "The promised amount cannot be lower than what was already paid.");

        await promiseRepository.UpdateAsync(promiseId, request.DueDate, request.PromisedAmount, PromiseRules.NormalizeNote(request.Note),
                                            request.RowVersion, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    public async Task CancelAsync(Guid promiseId, long rowVersion, CancellationToken cancellationToken)
    {
        var promise = await GetAsync(promiseId, cancellationToken);
        EnsureStoreAccess(promise.StoreId);
        if (!PromiseStatusCodes.IsStillActive(promise.StatusCode))
            throw new DomainException(ErrorKind.Conflict, "Only an open or partly paid promise can be cancelled.");

        await promiseRepository.CancelAsync(promiseId, rowVersion, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Promise {PromiseId} cancelled in company {CompanyId} by user {UserId}", promiseId, currentUser.CompanyId, currentUser.UserId);
    }

    private void EnsureStoreAccess(Guid storeId)
    {
        if (!currentUser.CanAccessStore(storeId))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to this store.");
    }

    private async Task EnsureStoreIsUsableAsync(Guid storeId, CancellationToken cancellationToken)
    {
        var usableStoreIds = await storeRepository.ActiveSellingStoreIdsAsync(currentUser.CompanyId, [storeId], cancellationToken);
        if (usableStoreIds.Count != 1)
            throw new DomainException(ErrorKind.Validation, "Choose an active store of this company.");
    }
}
