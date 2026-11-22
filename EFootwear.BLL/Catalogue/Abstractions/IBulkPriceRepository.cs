using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Common;

namespace EFootwear.BLL.Catalogue.Abstractions;

/// <summary>
/// Set-based price changes (docx 4.7, 4.8, 4.10) and the reversal of 4.12. Every apply method changes the prices, writes the price events with the
/// batch id in the same transaction, and returns the number of sizes changed. Rounding uses core.round_money (the company's FLOOR or NEAREST mode).
/// A StoreId means the store price (catalog.store_price_overrides); without it the company list price (catalog.variant_prices).
/// Implemented in EFootwear.DAL/Catalogue/Repositories/BulkPriceRepository.cs.
/// </summary>
public interface IBulkPriceRepository
{
    Task<PagedResult<PriceChangeRowResponse>> PreviewOldToNewAsync(OldToNewPriceChangeRequest request, PageRequest pageRequest, CancellationToken cancellationToken);
    Task<PagedResult<PriceChangeRowResponse>> PreviewPercentageAsync(PercentagePriceChangeRequest request, PageRequest pageRequest, CancellationToken cancellationToken);

    Task<int> ApplyOldToNewAsync(Guid batchId, OldToNewPriceChangeRequest request, Guid changedBy, CancellationToken cancellationToken);
    Task<int> ApplyPercentageAsync(Guid batchId, PercentagePriceChangeRequest request, Guid changedBy, CancellationToken cancellationToken);
    Task<int> ApplyNewSalePriceAsync(Guid batchId, SetNewSalePriceRequest request, Guid changedBy, CancellationToken cancellationToken);

    /// <summary>
    /// Puts back the sale price that a size had before its latest sale-price event, only when that event is the one that set the current price
    /// and has an old price. Returns how many sizes were restored and how many had no clear previous price.
    /// </summary>
    Task<(int RestoredCount, int SkippedCount)> RestorePreviousSalePricesAsync(Guid batchId, Guid storeId, IReadOnlyCollection<Guid> productIds,
                                                                              Guid changedBy, CancellationToken cancellationToken);
}
