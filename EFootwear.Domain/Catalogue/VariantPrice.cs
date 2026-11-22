namespace EFootwear.Domain.Catalogue;

/// <summary>
/// The company list prices of one variant. Table: catalog.variant_prices. Whole rupees (decision D-009). The sale price is the list (shelf)
/// price; a store can have its own price in <see cref="StorePriceOverride"/>, and a discount lowers the price a customer pays through
/// promo.effective_prices without touching the list price. The table has no touch trigger, so UpdatedAt and UpdatedBy are set by the application
/// and its row_ver is not used as a concurrency token (it never changes).
/// </summary>
public sealed class VariantPrice
{
    public Guid CompanyId { get; set; }
    public Guid VariantId { get; set; }
    public long PurchasePrice { get; set; }
    public long SalePrice { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
