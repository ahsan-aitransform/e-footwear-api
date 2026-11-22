namespace EFootwear.Domain.Catalogue;

/// <summary>
/// The classification fields the Change Product Attributes screen (docx 4.6) can reassign in bulk. Color is the docx improvement
/// (colour lives on the sizes, so the change touches every size of the product). Changing the category also moves the product to the
/// department of the new category, so a product never sits in a category of another department.
/// </summary>
public static class ProductAttributeFieldCodes
{
    public const string Unit = "UNIT";
    public const string Brand = "BRAND";
    public const string Category = "CATEGORY";
    public const string Gender = "GENDER";
    public const string Season = "SEASON";
    public const string Color = "COLOR";
    public const string Location = "LOCATION";
    public const string ProductType = "PRODUCT_TYPE";

    public static readonly IReadOnlyList<string> All = [Unit, Brand, Category, Gender, Season, Color, Location, ProductType];

    public static bool IsKnown(string? fieldCode) => fieldCode is not null && All.Contains(fieldCode);
}
