namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>
/// Saves an existing product. Sizes with a VariantId are updated, sizes without one are added, and existing sizes that are missing from the list are
/// removed (only when nothing but the catalogue itself uses them). RowVersion is the product version the client loaded.
/// </summary>
public sealed record UpdateProductRequest(
    string Name,
    string? NameUrdu,
    Guid DepartmentId,
    Guid CategoryId,
    Guid? BrandId,
    Guid? SeasonId,
    Guid? GenderId,
    Guid? ProductTypeId,
    Guid? UnitId,
    Guid? LocationId,
    decimal? Weight,
    string? Description,
    decimal TaxRate,
    string? TaxCode,
    string Status,
    long RowVersion,
    IReadOnlyList<ProductVariantInput> Variants);
