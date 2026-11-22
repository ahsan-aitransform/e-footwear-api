namespace EFootwear.Domain.Catalogue;

/// <summary>A sale price that applies to one store instead of the company list price. Table: catalog.store_price_overrides. Whole rupees.</summary>
public sealed class StorePriceOverride
{
    public Guid CompanyId { get; set; }
    public Guid StoreId { get; set; }
    public Guid VariantId { get; set; }
    public long SalePrice { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
