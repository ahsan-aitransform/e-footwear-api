namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>A product with its sizes and picture slots (docx 4.1: Basic Info, Extra and Images tabs). RowVersion is sent back on update.</summary>
public sealed record ProductDetailResponse(
    Guid Id,
    string Code,
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
    DateTimeOffset CreatedAt,
    long RowVersion,
    IReadOnlyList<ProductVariantDetailResponse> Variants,
    IReadOnlyList<ProductImageResponse> Images);
