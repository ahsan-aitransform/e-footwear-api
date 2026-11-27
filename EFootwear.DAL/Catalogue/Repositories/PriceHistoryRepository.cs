using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Common;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Catalogue;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Catalogue.Repositories;

/// <summary>The append-only price history (catalog.price_events). Rows are only ever added; the database refuses UPDATE and DELETE.</summary>
public sealed class PriceHistoryRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IPriceHistoryRepository
{
    private static readonly SortableFields<PriceChangeRow> SortablePriceHistoryFields = new SortableFields<PriceChangeRow>()
        .Allow("changedAt", row => row.ChangedAt);

    public async Task AddEventsAsync(IReadOnlyList<PriceEventWriteData> events, Guid changedBy, CancellationToken cancellationToken)
    {
        if (events.Count == 0) return;
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        databaseContext.PriceEvents.AddRange(events.Select(priceEvent => new PriceEvent
        {
            CompanyId = companyId, Id = Guid.CreateVersion7(), VariantId = priceEvent.VariantId, StoreId = priceEvent.StoreId, PriceType = priceEvent.PriceType,
            OldPrice = priceEvent.OldPrice, NewPrice = priceEvent.NewPrice, BatchId = priceEvent.BatchId, Reason = priceEvent.Reason, ChangedBy = changedBy,
        }));
    }

    public async Task<PagedResult<PriceHistoryEntryResponse>> ListByProductAsync(Guid productId, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var rows = from priceEvent in databaseContext.PriceEvents.AsNoTracking()
                   join variant in databaseContext.ProductVariants.AsNoTracking()
                       on new { priceEvent.CompanyId, Id = priceEvent.VariantId } equals new { variant.CompanyId, variant.Id }
                   join store in databaseContext.Stores.AsNoTracking()
                       on new { priceEvent.CompanyId, Id = priceEvent.StoreId } equals new { store.CompanyId, store.Id } into storeGroup
                   from store in storeGroup.DefaultIfEmpty()
                   join user in databaseContext.CompanyUsers.AsNoTracking()
                       on new { priceEvent.CompanyId, Id = priceEvent.ChangedBy } equals new { user.CompanyId, Id = (Guid?)user.Id } into userGroup
                   from user in userGroup.DefaultIfEmpty()
                   where priceEvent.CompanyId == companyId && variant.ProductId == productId
                   select new PriceHistoryRow
                   {
                       StoreId = priceEvent.StoreId,
                       StoreName = store == null ? null : store.Name, PriceType = priceEvent.PriceType, OldPrice = priceEvent.OldPrice, NewPrice = priceEvent.NewPrice,
                       BatchId = priceEvent.BatchId, Reason = priceEvent.Reason, ChangedAt = priceEvent.ChangedAt, ChangedByName = user == null ? null : user.FullName,
                   };

        // Everything saved together (same moment, store, reason and person) is one line: all sizes, purchase and sale price side by side.
        var changes = rows
            .GroupBy(row => new { row.ChangedAt, row.StoreId, row.StoreName, row.BatchId, row.Reason, row.ChangedByName })
            .Select(group => new PriceChangeRow
            {
                StoreId = group.Key.StoreId, StoreName = group.Key.StoreName, BatchId = group.Key.BatchId, Reason = group.Key.Reason,
                ChangedAt = group.Key.ChangedAt, ChangedByName = group.Key.ChangedByName,
                OldPurchaseLowest = group.Min(row => row.PriceType == PriceTypeCodes.Purchase ? row.OldPrice : null),
                OldPurchaseHighest = group.Max(row => row.PriceType == PriceTypeCodes.Purchase ? row.OldPrice : null),
                NewPurchaseLowest = group.Min(row => row.PriceType == PriceTypeCodes.Purchase ? (long?)row.NewPrice : null),
                NewPurchaseHighest = group.Max(row => row.PriceType == PriceTypeCodes.Purchase ? (long?)row.NewPrice : null),
                OldSaleLowest = group.Min(row => row.PriceType == PriceTypeCodes.Sale ? row.OldPrice : null),
                OldSaleHighest = group.Max(row => row.PriceType == PriceTypeCodes.Sale ? row.OldPrice : null),
                NewSaleLowest = group.Min(row => row.PriceType == PriceTypeCodes.Sale ? (long?)row.NewPrice : null),
                NewSaleHighest = group.Max(row => row.PriceType == PriceTypeCodes.Sale ? (long?)row.NewPrice : null),
            });

        var total = await changes.LongCountAsync(cancellationToken);
        var ordered = SortablePriceHistoryFields.TryOrder(changes, pageRequest.SortField, pageRequest.SortDirection, out var requestedOrder)
            ? requestedOrder.ThenBy(row => row.StoreId)
            : changes.OrderByDescending(row => row.ChangedAt).ThenBy(row => row.StoreId);
        var page = await ordered.Skip(pageRequest.SkippedItemCount).Take(pageRequest.PageSize).ToListAsync(cancellationToken);
        var items = page.Select(row => new PriceHistoryEntryResponse(
            row.StoreId == Guid.Empty ? null : row.StoreId, row.StoreId == Guid.Empty ? null : row.StoreName,
            row.OldPurchaseLowest == row.OldPurchaseHighest ? row.OldPurchaseLowest : null, row.NewPurchaseLowest, row.NewPurchaseHighest,
            row.OldSaleLowest == row.OldSaleHighest ? row.OldSaleLowest : null, row.NewSaleLowest, row.NewSaleHighest,
            row.BatchId, row.Reason, row.ChangedAt, row.ChangedByName)).ToList();
        return new PagedResult<PriceHistoryEntryResponse>(items, pageRequest.PageNumber, pageRequest.PageSize, total);
    }

    public async Task<bool> BatchExistsAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.PriceEvents.AsNoTracking().AnyAsync(priceEvent => priceEvent.CompanyId == companyId && priceEvent.BatchId == batchId, cancellationToken);
    }

    public async Task<int> CountVariantsInBatchAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.PriceEvents.AsNoTracking().Where(priceEvent => priceEvent.CompanyId == companyId && priceEvent.BatchId == batchId)
            .Select(priceEvent => priceEvent.VariantId).Distinct().CountAsync(cancellationToken);
    }

    private sealed class PriceHistoryRow
    {
        public Guid StoreId { get; set; }
        public string? StoreName { get; set; }
        public string PriceType { get; set; } = "";
        public long? OldPrice { get; set; }
        public long NewPrice { get; set; }
        public Guid? BatchId { get; set; }
        public string? Reason { get; set; }
        public DateTimeOffset ChangedAt { get; set; }
        public string? ChangedByName { get; set; }
    }

    private sealed class PriceChangeRow
    {
        public Guid StoreId { get; set; }
        public string? StoreName { get; set; }
        public Guid? BatchId { get; set; }
        public string? Reason { get; set; }
        public DateTimeOffset ChangedAt { get; set; }
        public string? ChangedByName { get; set; }
        public long? OldPurchaseLowest { get; set; }
        public long? OldPurchaseHighest { get; set; }
        public long? NewPurchaseLowest { get; set; }
        public long? NewPurchaseHighest { get; set; }
        public long? OldSaleLowest { get; set; }
        public long? OldSaleHighest { get; set; }
        public long? NewSaleLowest { get; set; }
        public long? NewSaleHighest { get; set; }
    }
}
