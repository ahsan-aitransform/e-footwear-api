using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Services;
using EFootwear.BLL.Promotions.Abstractions;
using EFootwear.BLL.Promotions.Contracts;
using EFootwear.Domain.Common;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Promotions.Services;

/// <summary>
/// Promotion conflicts (Part 18: priority resolution, "do not invent stacking behaviour"). When two or more promotions cover the same size in the same store, the engine does not
/// combine them and does not pick one: it sells the size at the normal price and opens a conflict. A person with the right chooses the promotion that wins; the choice is
/// kept while the same promotions cover the size, and a new or changed promotion opens the question again.
/// </summary>
public sealed class PromotionConflictService(
    IPromotionConflictRepository promotionConflictRepository,
    IPricingEngineRepository pricingEngineRepository,
    StoreAccessGuard storeAccessGuard,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ILogger<PromotionConflictService> logger)
{
    public async Task<PagedResult<PromotionConflictResponse>> ListOpenAsync(Guid? storeId, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (storeId is { } oneStore) await storeAccessGuard.EnsureAccessibleAsync(oneStore, cancellationToken);
        return await promotionConflictRepository.ListOpenAsync(storeId, pageRequest.Normalized(), cancellationToken);
    }

    public async Task ResolveAsync(Guid groupId, ResolvePromotionConflictRequest request, CancellationToken cancellationToken)
    {
        var candidateIds = await promotionConflictRepository.FindOpenGroupCandidateIdsAsync(groupId, cancellationToken)
                           ?? throw new DomainException(ErrorKind.NotFound, "This conflict is already resolved or no longer exists.");
        if (!candidateIds.Contains(request.ChosenRuleId))
            throw new DomainException(ErrorKind.Validation, "Choose one of the promotions that cover this product.");

        if (!await promotionConflictRepository.ResolveAsync(groupId, request.ChosenRuleId, currentUser.UserId, cancellationToken))
            throw new DomainException(ErrorKind.Conflict, "This conflict was resolved by somebody else.");
        await pricingEngineRepository.RefreshEffectivePricesAsync(cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Promotion conflict {GroupId} resolved with rule {RuleId} by user {UserId}", groupId, request.ChosenRuleId, currentUser.UserId);
    }
}
