namespace EFootwear.Domain.Catalogue;

/// <summary>Status of a product or a size (catalog.products.status, catalog.variants.status): Active products can be sold, inactive ones are hidden from sale.</summary>
public static class ProductStatusCodes
{
    public const string Active = "ACTIVE";
    public const string Inactive = "INACTIVE";

    public static bool IsKnown(string? statusCode) => statusCode is Active or Inactive;
}
