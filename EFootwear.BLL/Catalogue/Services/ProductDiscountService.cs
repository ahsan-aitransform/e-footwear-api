using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Common;
using EFootwear.Domain.Catalogue;
using EFootwear.Domain.Common;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Catalogue.Services;

/// <summary>
/// The discount screens of the Products menu: Change Discounts (docx 4.9, clear the last pieces), Product Discounts On Multiple Stores (4.11) and
/// Remove Discounts and Reverse Price (4.12). In this schema a discount is a promotion rule that the pricing engine turns into the price the POS
/// reads; the list price is never changed by a discount. The rules made here are marked PRODUCT_DISCOUNT.
///
/// One product has at most one such discount per store: a new discount replaces the old one in the stores it names. A discount that overlaps an
/// ordinary promotion is a conflict, which the engine reports instead of choosing silently (stacking is an open business decision), so every
/// answer carries the number of sizes in conflict.
/// </summary>
public sealed class ProductDiscountService(
    IProductDiscountRepository productDiscountRepository,
    IProductRepository productRepository,
    IBulkPriceRepository bulkPriceRepository,
    IPriceHistoryRepository priceHistoryRepository,
    IPricingEngineRepository pricingEngineRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<ProductDiscountService> logger)
{
    public Task<PagedResult<StockRangeDiscountRowResponse>> ListStockRangeCandidatesAsync(StockRangeDiscountRequest request, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        EnsureValidFilters(request);
        return productDiscountRepository.ListStockRangeCandidatesAsync(request, pageRequest.Normalized(), cancellationToken);
    }

    public async Task<ProductDiscountAppliedResponse> ApplyStockRangeDiscountAsync(StockRangeDiscountRequest request, CancellationToken cancellationToken)
    {
        EnsureValidFilters(request);
        EnsureValidDiscount(request);
        var batchId = request.ClientTransactionId ?? Guid.NewGuid();
        if (request.ClientTransactionId is not null && await productDiscountRepository.RuleBatchExistsAsync(batchId, cancellationToken))
            return AlreadyApplied(batchId);

        var channelId = await RequirePointOfSaleChannelAsync(cancellationToken);
        var productIds = await productDiscountRepository.ListStockRangeCandidateProductIdsAsync(request, cancellationToken);
        if (productIds.Count == 0)
            throw new DomainException(ErrorKind.Validation, "No product matches these filters in the chosen store.");
        if (request.DiscountType == ProductDiscountRuleCodes.DiscountTypeAmount
            && await productDiscountRepository.AnyStockRangeCandidatePricedAtOrBelowAsync(request, (long)request.DiscountValue!.Value, cancellationToken))
            throw new DomainException(ErrorKind.Validation, "The amount off is not below the price of every matching product. Lower the amount or narrow the filters.");

        await productDiscountRepository.RemoveProductsFromDiscountRulesAsync([request.StoreId], productIds, cancellationToken);
        var coveredProductCount = await productDiscountRepository.CreateStockRangeRuleAsync(batchId, request, channelId, currentUser.UserId, cancellationToken);
        await pricingEngineRepository.RefreshEffectivePricesAsync(cancellationToken);
        var conflictCount = await pricingEngineRepository.CountVariantsInConflictAsync(productIds, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("Discount {DiscountType} {DiscountValue} (batch {BatchId}) on {ProductCount} products in store {StoreId} by user {UserId}",
            request.DiscountType, request.DiscountValue, batchId, coveredProductCount, request.StoreId, currentUser.UserId);
        return new ProductDiscountAppliedResponse(batchId, coveredProductCount, 1, conflictCount, 0, 0, false);
    }

    public async Task<IReadOnlyList<ProductStoreDiscountRowResponse>> ListStoreDiscountRowsAsync(Guid productId, CancellationToken cancellationToken)
    {
        _ = await productRepository.FindDetailAsync(productId, cancellationToken) ?? throw new DomainException(ErrorKind.NotFound, "The product was not found.");
        return await productDiscountRepository.ListStoreDiscountRowsAsync(productId, cancellationToken);
    }

    public async Task<ProductDiscountAppliedResponse> ApplyDiscountOnStoresAsync(ProductDiscountOnStoresRequest request, CancellationToken cancellationToken)
    {
        if (request.StoreIds is null || request.StoreIds.Count == 0)
            throw new DomainException(ErrorKind.Validation, "Select at least one store.");
        var storeIds = request.StoreIds.Distinct().ToList();
        foreach (var storeId in storeIds) EnsureStoreIsAllowed(storeId);
        CataloguePriceRules.EnsureValidPercentage(request.DiscountPercent, "discount", allowZero: true);
        _ = await productRepository.FindDetailAsync(request.ProductId, cancellationToken) ?? throw new DomainException(ErrorKind.NotFound, "The product was not found.");

        var batchId = request.ClientTransactionId ?? Guid.NewGuid();
        if (request.ClientTransactionId is not null && await productDiscountRepository.RuleBatchExistsAsync(batchId, cancellationToken))
            return AlreadyApplied(batchId);

        await productDiscountRepository.RemoveProductsFromDiscountRulesAsync(storeIds, [request.ProductId], cancellationToken);
        if (request.DiscountPercent > 0)
        {
            var channelId = await RequirePointOfSaleChannelAsync(cancellationToken);
            await productDiscountRepository.CreateProductDiscountRulesAsync(batchId, request.ProductId, storeIds, request.DiscountPercent, channelId,
                                                                           currentUser.UserId, cancellationToken);
        }
        await pricingEngineRepository.RefreshEffectivePricesAsync(cancellationToken);
        var conflictCount = await pricingEngineRepository.CountVariantsInConflictAsync([request.ProductId], cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("Discount {DiscountPercent}% (batch {BatchId}) on product {ProductId} in {StoreCount} stores by user {UserId}",
            request.DiscountPercent, batchId, request.ProductId, storeIds.Count, currentUser.UserId);
        return new ProductDiscountAppliedResponse(batchId, 1, storeIds.Count, conflictCount, 0, 0, false);
    }

    public async Task<ProductDiscountAppliedResponse> RemoveDiscountsAsync(RemoveProductDiscountsRequest request, CancellationToken cancellationToken)
    {
        EnsureStoreIsAllowed(request.StoreId);
        if (request.ProductIds is null || request.ProductIds.Count == 0)
            throw new DomainException(ErrorKind.Validation, "Select at least one product.");
        if (request.ProductIds.Count > BulkPriceChangeService.MaximumProductsPerRequest)
            throw new DomainException(ErrorKind.Validation, $"Select at most {BulkPriceChangeService.MaximumProductsPerRequest} products at a time.");
        var productIds = request.ProductIds.Distinct().ToList();

        var batchId = request.ClientTransactionId ?? Guid.NewGuid();
        if (request.ClientTransactionId is not null
            && (await productDiscountRepository.RuleBatchExistsAsync(batchId, cancellationToken) || await priceHistoryRepository.BatchExistsAsync(batchId, cancellationToken)))
            return AlreadyApplied(batchId);

        var removedCount = await productDiscountRepository.RemoveProductsFromDiscountRulesAsync([request.StoreId], productIds, cancellationToken);
        var restoredCount = 0;
        var skippedCount = 0;
        if (request.AlsoRestorePreviousSalePrice)
            (restoredCount, skippedCount) = await bulkPriceRepository.RestorePreviousSalePricesAsync(batchId, request.StoreId, productIds, currentUser.UserId, cancellationToken);

        if (removedCount > 0 || restoredCount > 0)
            await pricingEngineRepository.RefreshEffectivePricesAsync(cancellationToken);
        var conflictCount = await pricingEngineRepository.CountVariantsInConflictAsync(productIds, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("Discounts removed from {ProductCount} products in store {StoreId} (batch {BatchId}, {RestoredCount} prices restored) by user {UserId}",
            productIds.Count, request.StoreId, batchId, restoredCount, currentUser.UserId);
        return new ProductDiscountAppliedResponse(batchId, productIds.Count, 1, conflictCount, restoredCount, skippedCount, false);
    }

    private void EnsureValidFilters(StockRangeDiscountRequest request)
    {
        EnsureStoreIsAllowed(request.StoreId);
        CataloguePriceRules.EnsureValidStockRange(request.MinimumStock, request.MaximumStock);
    }

    private static void EnsureValidDiscount(StockRangeDiscountRequest request)
    {
        if (request.DiscountType is not (ProductDiscountRuleCodes.DiscountTypePercent or ProductDiscountRuleCodes.DiscountTypeAmount))
            throw new DomainException(ErrorKind.Validation, "Choose a percentage or a fixed amount discount.");
        if (request.DiscountValue is not { } value)
            throw new DomainException(ErrorKind.Validation, "Enter the discount.");
        if (request.DiscountType == ProductDiscountRuleCodes.DiscountTypePercent)
        {
            CataloguePriceRules.EnsureValidPercentage(value, "discount");
            return;
        }
        if (value < 1 || value != decimal.Truncate(value) || value > CataloguePriceRules.MaximumPrice)
            throw new DomainException(ErrorKind.Validation, "The amount off must be a whole number of rupees above zero.");
    }

    private void EnsureStoreIsAllowed(Guid storeId)
    {
        if (storeId == Guid.Empty)
            throw new DomainException(ErrorKind.Validation, "Choose a store.");
        if (!currentUser.CanAccessStore(storeId))
            throw new DomainException(ErrorKind.Forbidden, "You cannot work in this store.", ErrorCodes.Forbidden);
    }

    private async Task<Guid> RequirePointOfSaleChannelAsync(CancellationToken cancellationToken) =>
        await productDiscountRepository.FindStorePointOfSaleChannelIdAsync(cancellationToken)
        ?? throw new DomainException(ErrorKind.Validation, "The company has no in-store point of sale channel, so a store discount cannot be created.");

    private static ProductDiscountAppliedResponse AlreadyApplied(Guid batchId) => new(batchId, 0, 0, 0, 0, 0, true);
}
