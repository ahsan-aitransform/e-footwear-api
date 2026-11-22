namespace EFootwear.Domain.Catalogue;

/// <summary>
/// One line of the price history. Table: catalog.price_events, append-only (a trigger refuses UPDATE and DELETE). The database does not
/// write these rows by itself: every code path that changes a price writes its own event with the old price, the new price, the store
/// (nil uuid = company list price), the batch id of a bulk operation, a reason and the user (Part 9.2: price changes are auditable).
/// </summary>
public sealed class PriceEvent
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public Guid VariantId { get; set; }

    /// <summary>The store the price applies to; the nil uuid means the company list price.</summary>
    public Guid StoreId { get; set; } = Guid.Empty;

    public string PriceType { get; set; } = PriceTypeCodes.Sale;
    public long? OldPrice { get; set; }
    public long NewPrice { get; set; }
    public Guid? BatchId { get; set; }
    public string? Reason { get; set; }
    public Guid? ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}
