namespace EFootwear.Domain.Catalogue;

/// <summary>One of the four picture slots of a product (catalog.product_images). The database keeps only the storage key; the file is in IFileStorage.</summary>
public sealed class ProductImage
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }

    /// <summary>1 to <see cref="ProductImageRules.SlotCount"/>.</summary>
    public short Slot { get; set; }

    public string StorageKey { get; set; } = "";
}
