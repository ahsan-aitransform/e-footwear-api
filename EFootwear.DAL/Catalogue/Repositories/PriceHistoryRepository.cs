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
    private static readonly SortableFields<PriceHistoryRow> SortablePriceHistoryFields = new SortableFields<PriceHistoryRow>()
        .Allow("changedAt", row => row.ChangedAt)
        .Allow("sizeCode", row => row.SizeCode)
        .Allow("priceType", row => row.PriceType)
        .Allow("newPrice", row => row.NewPrice);

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
                       Id = priceEvent.Id, VariantId = priceEvent.VariantId, SizeCode = variant.SizeCode, Barcode = variant.Barcode, StoreId = priceEvent.StoreId,
                       StoreName = store == null ? null : store.Name, PriceType = priceEvent.PriceType, OldPrice = priceEvent.OldPrice, NewPrice = priceEvent.NewPrice,
                       BatchId = priceEvent.BatchId, Reason = priceEvent.Reason, ChangedAt = priceEvent.ChangedAt, ChangedByName = user == null ? null : user.FullName,
                   };

        var total = await rows.LongCountAsync(cancellationToken);
        var ordered = SortablePriceHistoryFields.TryOrder(rows, pageRequest.SortField, pageRequest.SortDirection, out var requestedOrder)
            ? requestedOrder.ThenByDescending(row => row.Id)
            : rows.OrderByDescending(row => row.ChangedAt).ThenByDescending(row => row.Id);
        var page = await ordered.Skip(pageRequest.SkippedItemCount).Take(pageRequest.PageSize).ToListAsync(cancellationToken);
        var items = page.Select(row => new PriceHistoryEntryResponse(row.Id, row.VariantId, row.SizeCode, row.Barcode,
            row.StoreId == Guid.Empty ? null : row.StoreId, row.StoreId == Guid.Empty ? null : row.StoreName, row.PriceType, row.OldPrice, row.NewPrice,
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
        public Guid Id { get; set; }
        public Guid VariantId { get; set; }
        public string SizeCode { get; set; } = "";
        public string Barcode { get; set; } = "";
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
}
