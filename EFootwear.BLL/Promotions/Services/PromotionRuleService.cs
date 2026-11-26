using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Core.Services;
using EFootwear.BLL.Promotions.Abstractions;
using EFootwear.BLL.Promotions.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Promotions;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Promotions.Services;

/// <summary>
/// Special offers (docx 2.13), sales on stores (2.15) and sales on categories (2.16) (Part 18). All three are promotion rules; each screen reaches only its own kind. The price a rule
/// leads to is made by the database engine, which this service runs after every change, so the New Sale screen and the POS read the new price at once. Two promotions
/// covering one size are NOT combined and none wins silently: the engine sells the size at the normal price and opens a conflict for an administrator (Promotion Conflicts).
/// A promotion of chosen stores is changed by people who work in all of those stores; a promotion with no store (offers only: every store) only by people who may work in every store.
/// </summary>
public sealed class PromotionRuleService(
    IPromotionRuleRepository promotionRuleRepository,
    IPricingEngineRepository pricingEngineRepository,
    StoreAccessGuard storeAccessGuard,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<PromotionRuleService> logger)
{
    public Task<IReadOnlyList<PromotionRuleResponse>> ListAsync(string kindCode, CancellationToken cancellationToken)
    {
        EnsureKindIsKnown(kindCode);
        return promotionRuleRepository.ListAsync(kindCode, cancellationToken);
    }

    public Task<IReadOnlyList<PromotionCategoryResponse>> ListCategoryOptionsAsync(CancellationToken cancellationToken) =>
        promotionRuleRepository.ListCategoryOptionsAsync(cancellationToken);

    public async Task<PromotionRuleResponse> GetAsync(string kindCode, Guid ruleId, CancellationToken cancellationToken)
    {
        EnsureKindIsKnown(kindCode);
        return await promotionRuleRepository.FindAsync(kindCode, ruleId, cancellationToken)
               ?? throw new DomainException(ErrorKind.NotFound, "The promotion was not found.");
    }

    public async Task<PromotionRuleResponse> CreateAsync(string kindCode, SavePromotionRuleRequest request, CancellationToken cancellationToken)
    {
        EnsureKindIsKnown(kindCode);
        var cleaned = await CleanAndCheckAsync(kindCode, request, cancellationToken);
        await EnsureMayChangeAsync(cleaned.StoreIds ?? [], cancellationToken);

        var ruleId = await promotionRuleRepository.CreateAsync(kindCode, cleaned, currentUser.UserId, cancellationToken)
                     ?? throw new DomainException(ErrorKind.Conflict, "The company has no point of sale channel, so a sale on stores cannot be made yet.");
        await pricingEngineRepository.RefreshEffectivePricesAsync(cancellationToken);
        var created = await GetAsync(kindCode, ruleId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Promotion {RuleId} ({KindCode}) created by user {UserId}", ruleId, kindCode, currentUser.UserId);
        return created;
    }

    public async Task<PromotionRuleResponse> UpdateAsync(string kindCode, Guid ruleId, SavePromotionRuleRequest request, CancellationToken cancellationToken)
    {
        EnsureKindIsKnown(kindCode);
        if (request.RowVersion is not { } expectedRowVersion)
            throw new DomainException(ErrorKind.Validation, "Open the promotion again before saving it: its version is missing.");
        var existing = await GetAsync(kindCode, ruleId, cancellationToken);
        await EnsureMayChangeAsync(existing.Stores.Select(store => store.StoreId).ToList(), cancellationToken);

        var cleaned = await CleanAndCheckAsync(kindCode, request, cancellationToken);
        await EnsureMayChangeAsync(cleaned.StoreIds ?? [], cancellationToken);

        if (!await promotionRuleRepository.UpdateAsync(kindCode, ruleId, cleaned, expectedRowVersion, currentUser.UserId, cancellationToken))
            throw new DomainException(ErrorKind.Conflict, "Somebody else changed this promotion. Open it again and repeat your change.");
        await pricingEngineRepository.RefreshEffectivePricesAsync(cancellationToken);
        var updated = await GetAsync(kindCode, ruleId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Promotion {RuleId} ({KindCode}) changed by user {UserId}", ruleId, kindCode, currentUser.UserId);
        return updated;
    }

    public async Task DeleteAsync(string kindCode, Guid ruleId, CancellationToken cancellationToken)
    {
        EnsureKindIsKnown(kindCode);
        var existing = await GetAsync(kindCode, ruleId, cancellationToken);
        await EnsureMayChangeAsync(existing.Stores.Select(store => store.StoreId).ToList(), cancellationToken);
        if (!await promotionRuleRepository.DeleteAsync(kindCode, ruleId, currentUser.UserId, cancellationToken))
            throw new DomainException(ErrorKind.NotFound, "The promotion was not found.");
        await pricingEngineRepository.RefreshEffectivePricesAsync(cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Promotion {RuleId} ({KindCode}) deleted by user {UserId}", ruleId, kindCode, currentUser.UserId);
    }

    /// <summary>
    /// Runs the pricing engine now. A promotion that starts or ends at a given minute changes the prices only when the engine runs; nothing runs it at that minute, so this is the
    /// action a person takes (or an operator schedules) to apply the schedule.
    /// </summary>
    public async Task RefreshPricesAsync(CancellationToken cancellationToken)
    {
        await pricingEngineRepository.RefreshEffectivePricesAsync(cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Promotion prices refreshed by user {UserId}", currentUser.UserId);
    }

    private async Task<SavePromotionRuleRequest> CleanAndCheckAsync(string kindCode, SavePromotionRuleRequest request, CancellationToken cancellationToken)
    {
        var name = PromotionRules.NormalizeName(request.Name);
        PromotionRules.EnsureNameIsValid(name, kindCode == PromotionKindCodes.SpecialOffer ? "special offer" : "sale");
        PromotionRules.EnsureWindowIsValid(request.StartsAt, request.EndsAt);

        var storeIds = (request.StoreIds ?? []).Distinct().ToList();
        var categoryIds = (request.CategoryIds ?? []).Distinct().ToList();
        decimal? percentage = request.DiscountPercent;

        switch (kindCode)
        {
            case PromotionKindCodes.SpecialOffer:
                percentage = null;
                categoryIds = [];
                break;
            case PromotionKindCodes.SaleOnStores:
                PromotionRules.EnsurePercentageIsValid(percentage);
                if (storeIds.Count == 0) throw new DomainException(ErrorKind.Validation, "Choose at least one store.");
                categoryIds = [];
                break;
            default:
                PromotionRules.EnsurePercentageIsValid(percentage);
                if (storeIds.Count == 0) throw new DomainException(ErrorKind.Validation, "Choose at least one store.");
                if (categoryIds.Count == 0) throw new DomainException(ErrorKind.Validation, "Choose at least one category.");
                if (await promotionRuleRepository.CountExistingCategoriesAsync(categoryIds, cancellationToken) != categoryIds.Count)
                    throw new DomainException(ErrorKind.Validation, "One of the chosen categories does not exist.");
                break;
        }

        foreach (var storeId in storeIds) await storeAccessGuard.EnsureAccessibleAsync(storeId, cancellationToken);
        return request with { Name = name, DiscountPercent = percentage, StoreIds = storeIds, CategoryIds = categoryIds };
    }

    // One store after the other: the checks share one database connection, which cannot run two commands at the same time.
    private async Task EnsureMayChangeAsync(IReadOnlyList<Guid> storeIds, CancellationToken cancellationToken)
    {
        if (storeIds.Count == 0)
        {
            if (!currentUser.HasAccessToAllStores)
                throw new DomainException(ErrorKind.Forbidden, "A promotion for every store can only be changed by somebody who may work in every store.");
            return;
        }
        foreach (var storeId in storeIds) await storeAccessGuard.EnsureAccessibleAsync(storeId, cancellationToken);
    }

    private static void EnsureKindIsKnown(string kindCode)
    {
        if (!PromotionKindCodes.IsValid(kindCode)) throw new DomainException(ErrorKind.Validation, "Unknown kind of promotion.");
    }
}
