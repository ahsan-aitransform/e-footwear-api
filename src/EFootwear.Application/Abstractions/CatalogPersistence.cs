namespace EFootwear.Application.Abstractions;

// ---------------------------------------------------------------------------------------------------- catalog

public sealed record ProductFilter(long? CategoryId, long? BrandId, long? DepartmentId, string? Status, long? StoreId);

public sealed record ProductInput(string? Code, string Name, string? NameUr, long DepartmentId, long CategoryId, long? BrandId, long? SeasonId, long? GenderId,
                                  long? ProductTypeId, long? UnitId, long? LocationId, decimal? Weight, string? Description, decimal TaxRate, string Status);

public sealed record VariantInput(long? ColorId, long? SizeGroupId, string SizeCode, string Barcode, string Status);

public interface ICatalogRepository
{
    Task<Page<Row>> ProductsAsync(ProductFilter f, PageQuery q, CancellationToken ct);
    Task<Row?> ProductAsync(long id, CancellationToken ct);
    Task<long> InsertProductAsync(long companyId, ProductInput p, string? code, CancellationToken ct);
    Task<bool> UpdateProductAsync(long id, ProductInput p, long? rowVer, CancellationToken ct);
    Task SoftDeleteProductAsync(long id, long userId, CancellationToken ct);
    Task<decimal> ProductStockAsync(long productId, CancellationToken ct);

    Task<List<Row>> VariantsAsync(long productId, CancellationToken ct);
    Task<Row?> VariantAsync(long id, CancellationToken ct);
    Task<long> InsertVariantAsync(long companyId, long productId, VariantInput v, CancellationToken ct);
    Task<bool> UpdateVariantAsync(long id, string barcode, string status, long? rowVer, CancellationToken ct);
    Task SoftDeleteVariantAsync(long id, long userId, CancellationToken ct);
    Task<decimal> VariantStockAsync(long variantId, CancellationToken ct);

    Task<Row?> PriceAsync(long variantId, CancellationToken ct);
    Task UpsertPricesAsync(long companyId, long variantId, long purchase, long sale, CancellationToken ct);
    Task InsertPriceEventAsync(long companyId, long variantId, long storeId, string type, long? oldPrice, long newPrice, Guid? batch, string? reason, long userId, CancellationToken ct);
    Task UpsertStoreOverrideAsync(long companyId, long storeId, long variantId, long salePrice, long userId, CancellationToken ct);
    Task<long?> StoreOverrideAsync(long storeId, long variantId, CancellationToken ct);
    Task DeleteStoreOverrideAsync(long storeId, long variantId, CancellationToken ct);
    Task<List<Row>> StoreOverridesAsync(long variantId, CancellationToken ct);
    Task<List<Row>> PriceEventsAsync(long? variantId, Guid? batch, int limit, CancellationToken ct);
    Task<List<Row>> TargetVariantsAsync(IReadOnlyCollection<long> variantIds, IReadOnlyCollection<long> productIds, long? categoryId, long? brandId, CancellationToken ct);

    Task<Row?> LookupAsync(string barcode, long storeId, CancellationToken ct);
    Task<List<Row>> SearchVariantsAsync(string q, long storeId, int limit, CancellationToken ct);

    Task<List<Row>> PriceBandsAsync(CancellationToken ct);
    Task<long> InsertPriceBandAsync(long companyId, string name, long min, long? max, int sort, CancellationToken ct);
    Task<bool> UpdatePriceBandAsync(long id, string name, long min, long? max, int sort, long? rowVer, CancellationToken ct);
    Task DeletePriceBandAsync(long id, long userId, CancellationToken ct);
}

public interface IStockRepository
{
    Task<Page<Row>> BalancesAsync(long? storeId, long? productId, long? categoryId, string? search, bool onlyPositive, PageQuery q, CancellationToken ct);
    Task<List<Row>> BySizeAsync(long productId, CancellationToken ct);
    Task<Page<Row>> LedgerAsync(long? variantId, long? storeId, DateOnly? from, DateOnly? to, string? docType, PageQuery q, CancellationToken ct);
    Task<long> ChannelIdAsync(string code, CancellationToken ct);
}

// ---------------------------------------------------------------------------------------------------- promotions

public sealed record RuleInput(string Name, string DiscountType, decimal DiscountValue, string TargetType, decimal? StockMin, decimal? StockMax,
                               DateTimeOffset StartsAt, DateTimeOffset? EndsAt, int Priority);

public sealed record RuleTarget(long? CategoryId, long? ProductId, long? VariantId, decimal? DiscountOverride);

public sealed record BundleItemInput(long VariantId, decimal Qty, long NetUnitPrice);

public interface IPromoRepository
{
    Task<Page<Row>> RulesAsync(string? status, PageQuery q, CancellationToken ct);
    Task<Row?> RuleAsync(long id, CancellationToken ct);
    Task<long> InsertRuleAsync(long companyId, RuleInput r, CancellationToken ct);
    Task<bool> UpdateRuleAsync(long id, RuleInput r, long? rowVer, CancellationToken ct);
    Task SetRuleStatusAsync(long id, string status, CancellationToken ct);
    Task SoftDeleteRuleAsync(long id, long userId, CancellationToken ct);
    Task ReplaceRuleScopeAsync(long companyId, long ruleId, IReadOnlyCollection<RuleTarget> targets, IReadOnlyCollection<long> stores, IReadOnlyCollection<long> channels, CancellationToken ct);

    Task<Page<Row>> BundlesAsync(PageQuery q, CancellationToken ct);
    Task<Row?> BundleAsync(long id, CancellationToken ct);
    Task<long> InsertBundleAsync(long companyId, string name, string barcode, long price, DateTimeOffset starts, DateTimeOffset? ends, CancellationToken ct);
    Task ReplaceBundleItemsAsync(long companyId, long bundleId, IReadOnlyCollection<BundleItemInput> items, CancellationToken ct);
    Task SetBundleStatusAsync(long id, string status, CancellationToken ct);
    Task SoftDeleteBundleAsync(long id, long userId, CancellationToken ct);

    Task<List<Row>> ChannelsAsync(CancellationToken ct);
    Task<List<Row>> ConflictsAsync(CancellationToken ct);
    Task<Row?> ConflictAsync(long id, CancellationToken ct);
    Task ResolveConflictAsync(long id, long ruleId, long userId, CancellationToken ct);

    Task<long> RebuildEffectivePricesAsync(long companyId, CancellationToken ct);
    Task<bool> NeedsRefreshAsync(long companyId, CancellationToken ct);
    Task<List<long>> ActiveCompanyIdsAsync(CancellationToken ct);
}
