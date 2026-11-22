using EFootwear.Domain.Common;

namespace EFootwear.Domain.Catalogue;

/// <summary>
/// One sellable size (and colour) of a product, identified by its barcode. Table: catalog.variants. The barcode is unique inside the
/// company (and also unique against deal bundles, checked by a database trigger). Rows are soft-deleted.
/// </summary>
public sealed class ProductVariant : IHasRowVersion
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ColorId { get; set; }
    public Guid? SizeGroupId { get; set; }
    public string SizeCode { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string Status { get; set; } = ProductStatusCodes.Active;
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public long RowVersion { get; set; }
}
