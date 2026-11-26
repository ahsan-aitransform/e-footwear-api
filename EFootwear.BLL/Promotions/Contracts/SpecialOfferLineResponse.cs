namespace EFootwear.BLL.Promotions.Contracts;

/// <summary>One size (barcode) of a special offer with the discount percentage it gets (docx 2.14: Barcode, Product Name, Category, Gender, Season, Company, Discount %).</summary>
public sealed record SpecialOfferLineResponse(
    Guid LineId,
    Guid VariantId,
    string Barcode,
    string ProductCode,
    string ProductName,
    string SizeCode,
    string? ColorName,
    string CategoryName,
    string? GenderName,
    string? SeasonName,
    string? BrandName,
    decimal DiscountPercent);
