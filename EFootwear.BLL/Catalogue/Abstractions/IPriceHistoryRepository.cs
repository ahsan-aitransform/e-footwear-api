using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Common;

namespace EFootwear.BLL.Catalogue.Abstractions;

/// <summary>The append-only price history (catalog.price_events). Implemented in EFootwear.DAL/Catalogue/Repositories/PriceHistoryRepository.cs.</summary>
public interface IPriceHistoryRepository
{
    Task AddEventsAsync(IReadOnlyList<PriceEventWriteData> events, Guid changedBy, CancellationToken cancellationToken);
    Task<PagedResult<PriceHistoryEntryResponse>> ListByProductAsync(Guid productId, PageRequest pageRequest, CancellationToken cancellationToken);

    /// <summary>True when a bulk operation with this batch id already wrote events (a retry of the same request).</summary>
    Task<bool> BatchExistsAsync(Guid batchId, CancellationToken cancellationToken);

    Task<int> CountVariantsInBatchAsync(Guid batchId, CancellationToken cancellationToken);
}
