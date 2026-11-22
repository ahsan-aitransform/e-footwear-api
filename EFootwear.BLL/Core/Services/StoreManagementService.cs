using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Core.Contracts;
using EFootwear.BLL.Platform.Abstractions;
using EFootwear.BLL.Platform.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Core;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Core.Services;

/// <summary>
/// Store administration by the company: create, edit, deactivate and reactivate stores (ADD-05). The head office is never
/// deactivated. The plan limit (platform.plans.max_stores) is checked here for a clear message and again by a database trigger.
/// </summary>
public sealed class StoreManagementService(
    IStoreManagementRepository storeManagementRepository,
    IPlanRepository planRepository,
    RequestContext requestContext,
    IUnitOfWork unitOfWork,
    ILogger<StoreManagementService> logger)
{
    public Task<IReadOnlyList<StoreDetailResponse>> ListAsync(CancellationToken cancellationToken) =>
        storeManagementRepository.ListAllAsync(requestContext.CompanyId, cancellationToken);

    public Task<PlanUsageResponse> GetPlanUsageAsync(CancellationToken cancellationToken) =>
        GetPlanUsageOfOwnCompanyAsync(cancellationToken);

    public async Task<Guid> CreateAsync(CreateManagedStoreRequest request, CancellationToken cancellationToken)
    {
        StoreRules.EnsureValid(request.Code, request.Name);
        var companyId = requestContext.CompanyId;
        var code = request.Code.Trim();

        var usage = await GetPlanUsageOfOwnCompanyAsync(cancellationToken);
        EnsureWithinStoreLimit(usage);
        if (await storeManagementRepository.CodeExistsAsync(companyId, code, cancellationToken))
            throw new DomainException(ErrorKind.Conflict, $"Store code {code} already exists in this company.");

        var storeId = await storeManagementRepository.CreateAsync(companyId, code, request.Name.Trim(),
            StoreRules.NormalizeAddress(request.Address), StoreRules.NormalizePhone(request.Phone), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Store {StoreId} ({Code}) created in company {CompanyId} by user {UserId}",
            storeId, code, companyId, requestContext.UserId);
        return storeId;
    }

    public async Task UpdateAsync(Guid storeId, UpdateManagedStoreRequest request, CancellationToken cancellationToken)
    {
        var companyId = requestContext.CompanyId;
        var store = await storeManagementRepository.FindAsync(companyId, storeId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "Store not found.");
        if (request.ExpectedRowVersion != store.RowVersion)
            throw new DomainException(ErrorKind.Conflict, "The store was changed by someone else. Reload it and try again.", ErrorCodes.ConcurrentUpdate);

        await storeManagementRepository.UpdateAsync(companyId, storeId, StoreRules.EnsureValidName(request.Name),
            StoreRules.NormalizeAddress(request.Address), StoreRules.NormalizePhone(request.Phone), request.ExpectedRowVersion, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Store {StoreId} updated by user {UserId}", storeId, requestContext.UserId);
    }

    public async Task SetActiveAsync(Guid storeId, bool isActive, CancellationToken cancellationToken)
    {
        var companyId = requestContext.CompanyId;
        var store = await storeManagementRepository.FindAsync(companyId, storeId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "Store not found.");
        if (store.IsHeadOffice && !isActive)
            throw new DomainException(ErrorKind.Validation, "The head office cannot be deactivated.");
        if (store.IsActive == isActive) return;

        if (isActive && !store.IsHeadOffice) EnsureWithinStoreLimit(await GetPlanUsageOfOwnCompanyAsync(cancellationToken));
        await storeManagementRepository.SetActiveAsync(companyId, storeId, isActive, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Store {StoreId} {State} by user {UserId}", storeId, isActive ? "activated" : "deactivated", requestContext.UserId);
    }

    private async Task<PlanUsageResponse> GetPlanUsageOfOwnCompanyAsync(CancellationToken cancellationToken) =>
        await planRepository.GetUsageForCompanyAsync(requestContext.CompanyId, cancellationToken)
        ?? throw new DomainException(ErrorKind.NotFound, "Company not found.");

    private static void EnsureWithinStoreLimit(PlanUsageResponse usage)
    {
        if (usage.ActiveStores >= usage.MaximumStores)
            throw new DomainException(ErrorKind.Conflict,
                $"Your plan {usage.PlanName} allows at most {usage.MaximumStores} active stores.", ErrorCodes.PlanLimitReached);
    }
}
