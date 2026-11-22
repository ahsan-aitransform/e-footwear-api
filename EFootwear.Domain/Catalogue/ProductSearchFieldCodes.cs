namespace EFootwear.Domain.Catalogue;

/// <summary>The field the Search Product screen (docx 4.3) looks in, and how the typed text must match. Sent as text in the query string.</summary>
public static class ProductSearchFieldCodes
{
    public const string Name = "NAME";
    public const string Barcode = "BARCODE";
    public const string Department = "DEPARTMENT";
    public const string Category = "CATEGORY";
    public const string Brand = "BRAND";
    public const string SalePrice = "SALE_PRICE";
    public const string Color = "COLOR";

    public static bool IsKnown(string? fieldCode) => fieldCode is Name or Barcode or Department or Category or Brand or SalePrice or Color;
}
