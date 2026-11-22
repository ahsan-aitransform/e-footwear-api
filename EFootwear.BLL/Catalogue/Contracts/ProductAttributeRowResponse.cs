namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>A product listed by Change Product Attributes (docx 4.6 grid: Product Id, Product Name, Unit, Company, Category, Gender, Season; Color is the docx improvement).</summary>
public sealed record ProductAttributeRowResponse(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string? UnitName,
    string? BrandName,
    string? CategoryName,
    string? GenderName,
    string? SeasonName,
    string? ColorName);
