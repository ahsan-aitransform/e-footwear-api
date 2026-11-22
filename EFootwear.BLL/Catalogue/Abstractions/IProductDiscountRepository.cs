using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Common;

namespace EFootwear.BLL.Catalogue.Abstractions;

/// <summary>
/// Product discounts as promotion rules (promo.rules created_from PRODUCT_DISCOUNT, promo.rule_targets, promo.rule_stores, promo.rule_channels).
/// The pricing engine, not this repository, turns the rules into prices. Implemented in EFootwear.DAL/Catalogue/Repositories/ProductDiscountRepository.cs.
/// </summary>
public interface IProductDiscountRepository
{
    /// <summary>The id of the in-store point of sale channel (store-scoped rules must name it); null when the company has none.</summary>
    Task<Guid?> FindStorePointOfSaleChannelIdAsync(CancellationToken cancellationToken);

    Task<PagedResult<StockRangeDiscountRowResponse>> ListStockRangeCandidatesAsync(StockRangeDiscountRequest request, PageRequest pageRequest, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductStoreDiscountRowResponse>> ListStoreDiscountRowsAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>True when rules of this batch exist (a retry of the same request).</summary>
    Task<bool> RuleBatchExistsAsync(Guid batchId, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the products out of every product-discount rule of the stores (the other targets and stores of those rules stay) and cancels rules that
    /// end up with no target. Returns how many product-store discounts were removed.
    /// </summary>
    Task<int> RemoveProductsFromDiscountRulesAsync(IReadOnlyCollection<Guid> storeIds, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);

    /// <summary>Creates one published rule for the store with a stock range and the given products as targets. Returns the products covered.</summary>
    Task<int> CreateStockRangeRuleAsync(Guid batchId, StockRangeDiscountRequest request, Guid channelId, Guid createdBy, CancellationToken cancellationToken);

    /// <summary>Creates one published rule per store for one product.</summary>
    Task CreateProductDiscountRulesAsync(Guid batchId, Guid productId, IReadOnlyCollection<Guid> storeIds, decimal discountPercent, Guid channelId,
                                         Guid createdBy, CancellationToken cancellationToken);

    /// <summary>The product ids of the stock-range candidates (all pages), used to remove the products from older rules first.</summary>
    Task<IReadOnlyList<Guid>> ListStockRangeCandidateProductIdsAsync(StockRangeDiscountRequest request, CancellationToken cancellationToken);
}
