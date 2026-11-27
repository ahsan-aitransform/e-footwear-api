using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Common;
using EFootwear.Domain.Catalogue;
using EFootwear.Domain.Common;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Catalogue.Services;

/// <summary>
/// Change Prices (Old to New) docx 4.7, Change Sale Price (In %) docx 4.8 and the new sale price of Product Discounts docx 4.10.
/// Each screen previews the sizes it would change, then applies the change in one transaction: prices, price events (with the batch id) and the
/// pricing engine rebuild. A request that carries a ClientTransactionId is idempotent: a retry finds its own price events and changes nothing twice.
/// With a StoreId the store price changes (catalog.store_price_overrides); without one the company list price. Purchase prices are company-wide only.
/// </summary>
public sealed class BulkPriceChangeService(
    IBulkPriceRepository bulkPriceRepository,
    IPriceHistoryRepository priceHistoryRepository,
    IPricingEngineRepository pricingEngineRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<BulkPriceChangeService> logger)
{
    /// <summary>A sanity limit on one request.</summary>
    public const int MaximumProductsPerRequest = 5000;

    public Task<PagedResult<PriceChangeRowResponse>> PreviewOldToNewAsync(OldToNewPriceChangeRequest request, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        EnsureValid(request);
        return bulkPriceRepository.PreviewOldToNewAsync(request, pageRequest.Normalized(), cancellationToken);
    }

    /// <summary>Without a percentage the preview lists the matching sizes with their price unchanged; applying always needs one.</summary>
    public Task<PagedResult<PriceChangeRowResponse>> PreviewPercentageAsync(PercentagePriceChangeRequest request, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (request is { IncreasePercent: null, DecreasePercent: null }) EnsureStoreIsAllowed(request.StoreId);
        else EnsureValid(request);
        return bulkPriceRepository.PreviewPercentageAsync(request, pageRequest.Normalized(), cancellationToken);
    }

    public async Task<PriceChangeAppliedResponse> ApplyOldToNewAsync(OldToNewPriceChangeRequest request, CancellationToken cancellationToken)
    {
        EnsureValid(request);
        var batchId = request.ClientTransactionId ?? Guid.NewGuid();
        if (await WasAlreadyAppliedAsync(request.ClientTransactionId, cancellationToken) is { } earlier) return earlier;

        var changedCount = await bulkPriceRepository.ApplyOldToNewAsync(batchId, request, currentUser.UserId, cancellationToken);
        return await FinishAsync(batchId, changedCount, "old-to-new", cancellationToken);
    }

    public async Task<PriceChangeAppliedResponse> ApplyPercentageAsync(PercentagePriceChangeRequest request, CancellationToken cancellationToken)
    {
        EnsureValid(request);
        var batchId = request.ClientTransactionId ?? Guid.NewGuid();
        if (await WasAlreadyAppliedAsync(request.ClientTransactionId, cancellationToken) is { } earlier) return earlier;

        var changedCount = await bulkPriceRepository.ApplyPercentageAsync(batchId, request, currentUser.UserId, cancellationToken);
        return await FinishAsync(batchId, changedCount, "percentage", cancellationToken);
    }

    public async Task<PriceChangeAppliedResponse> SetNewSalePriceAsync(SetNewSalePriceRequest request, CancellationToken cancellationToken)
    {
        EnsureStoreIsAllowed(request.StoreId);
        if (request.ProductIds is null || request.ProductIds.Count == 0)
            throw new DomainException(ErrorKind.Validation, "Select at least one product.");
        if (request.ProductIds.Count > MaximumProductsPerRequest)
            throw new DomainException(ErrorKind.Validation, $"Select at most {MaximumProductsPerRequest} products at a time.");
        CataloguePriceRules.EnsureValidPrice(request.NewSalePrice, "new sale price");
        var distinctRequest = request with { ProductIds = request.ProductIds.Distinct().ToList() };

        var batchId = request.ClientTransactionId ?? Guid.NewGuid();
        if (await WasAlreadyAppliedAsync(request.ClientTransactionId, cancellationToken) is { } earlier) return earlier;

        var changedCount = await bulkPriceRepository.ApplyNewSalePriceAsync(batchId, distinctRequest, currentUser.UserId, cancellationToken);
        return await FinishAsync(batchId, changedCount, "new sale price", cancellationToken);
    }

    private void EnsureValid(OldToNewPriceChangeRequest request)
    {
        EnsureStoreIsAllowed(request.StoreId);
        if (!PriceTypeCodes.IsKnown(request.PriceType))
            throw new DomainException(ErrorKind.Validation, "Choose which price to change: sale price or purchase price.");
        if (request.StoreId is not null && request.PriceType == PriceTypeCodes.Purchase)
            throw new DomainException(ErrorKind.Validation, "Purchase prices are kept for the whole company, not per store. Choose all stores.");
        CataloguePriceRules.EnsureValidPrice(request.OldPrice, "old price");
        CataloguePriceRules.EnsureValidPrice(request.NewPrice, "new price");
        if (request.OldPrice == request.NewPrice)
            throw new DomainException(ErrorKind.Validation, "The new price is the same as the old price.");
    }

    private void EnsureValid(PercentagePriceChangeRequest request)
    {
        EnsureStoreIsAllowed(request.StoreId);
        var hasIncrease = request.IncreasePercent is not null;
        var hasDecrease = request.DecreasePercent is not null;
        if (hasIncrease == hasDecrease)
            throw new DomainException(ErrorKind.Validation, "Enter either an increase or a decrease percentage, not both and not neither.");
        if (request.IncreasePercent is { } increase) CataloguePriceRules.EnsureValidIncreasePercentage(increase);
        if (request.DecreasePercent is { } decrease) CataloguePriceRules.EnsureValidDecreasePercentage(decrease);
    }

    private void EnsureStoreIsAllowed(Guid? storeId)
    {
        if (storeId is not { } id) return;
        if (id == Guid.Empty)
            throw new DomainException(ErrorKind.Validation, "Choose a store.");
        if (!currentUser.CanAccessStore(id))
            throw new DomainException(ErrorKind.Forbidden, "You cannot work in this store.", ErrorCodes.Forbidden);
    }

    private async Task<PriceChangeAppliedResponse?> WasAlreadyAppliedAsync(Guid? clientTransactionId, CancellationToken cancellationToken)
    {
        if (clientTransactionId is not { } batchId || !await priceHistoryRepository.BatchExistsAsync(batchId, cancellationToken)) return null;
        var changedCount = await priceHistoryRepository.CountVariantsInBatchAsync(batchId, cancellationToken);
        return new PriceChangeAppliedResponse(batchId, changedCount, 0, true);
    }

    private async Task<PriceChangeAppliedResponse> FinishAsync(Guid batchId, int changedCount, string operation, CancellationToken cancellationToken)
    {
        if (changedCount > 0)
            await pricingEngineRepository.RefreshEffectivePricesAsync(cancellationToken);
        var conflictCount = await pricingEngineRepository.CountVariantsInConflictAsync(null, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Bulk price change ({Operation}) batch {BatchId} changed {ChangedCount} sizes in company {CompanyId} by user {UserId}",
            operation, batchId, changedCount, currentUser.CompanyId, currentUser.UserId);
        return new PriceChangeAppliedResponse(batchId, changedCount, conflictCount, false);
    }
}
