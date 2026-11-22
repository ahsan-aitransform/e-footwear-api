using EFootwear.Domain.Common;

namespace EFootwear.Domain.Catalogue;

/// <summary>
/// A product (article). Table: catalog.products. The product holds what is shared by every size and colour; each sellable
/// size is a <see cref="ProductVariant"/> with its own barcode. The code is allocated by the database (company-wide) and is
/// what the invoices print. Rows are soft-deleted.
/// </summary>
public sealed class Product : IHasRowVersion
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? NameUrdu { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid CategoryId { get; set; }
    public Guid? BrandId { get; set; }
    public Guid? SeasonId { get; set; }
    public Guid? GenderId { get; set; }
    public Guid? ProductTypeId { get; set; }
    public Guid? UnitId { get; set; }
    public Guid? LocationId { get; set; }
    public decimal? Weight { get; set; }
    public string? Description { get; set; }

    /// <summary>Percentage 0 to 100. Tax treatment is a business decision; the value is only stored.</summary>
    public decimal TaxRate { get; set; }

    public string? TaxCode { get; set; }
    public string Status { get; set; } = ProductStatusCodes.Active;

    /// <summary>JSON for configurable attributes that have no column (Part 7). Always a JSON object.</summary>
    public string AttributesJson { get; set; } = "{}";

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public long RowVersion { get; set; }
}
