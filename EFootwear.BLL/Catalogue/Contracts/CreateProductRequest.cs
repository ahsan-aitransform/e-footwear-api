namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>Creates a product with at least one size (docx 4.1.6). The code is allocated by the system. Prices are whole rupees.</summary>
public sealed record CreateProductRequest(
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
    IReadOnlyList<ProductVariantInput> Variants);
