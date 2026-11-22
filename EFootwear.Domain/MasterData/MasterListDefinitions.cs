namespace EFootwear.Domain.MasterData;

/// <summary>
/// The ten simple master lists of a company (docx 2.4 to 2.11, 2.17, plus the size groups and product types that keep sizes and
/// product kinds configurable, Part 7). Categories and sizes have their own shape and are not listed here.
/// </summary>
public static class MasterListDefinitions
{
    public static readonly MasterListDefinition Department = new("departments", "departments", "Department");
    public static readonly MasterListDefinition Brand = new("brands", "brands", "Company / Brand");
    public static readonly MasterListDefinition Season = new("seasons", "seasons", "Season");
    public static readonly MasterListDefinition Gender = new("genders", "genders", "Gender");
    public static readonly MasterListDefinition Color = new("colors", "colors", "Color");
    public static readonly MasterListDefinition Location = new("locations", "locations", "Location");
    public static readonly MasterListDefinition Unit = new("units", "units", "Unit");
    public static readonly MasterListDefinition City = new("cities", "cities", "City / Area", WarnAboutNearDuplicateNames: true);
    public static readonly MasterListDefinition SizeGroup = new("size-groups", "size_groups", "Size Group");
    public static readonly MasterListDefinition ProductType = new("product-types", "product_types", "Product Type");

    public static IReadOnlyList<MasterListDefinition> All { get; } =
        [Department, Brand, Season, Gender, Color, Location, Unit, City, SizeGroup, ProductType];
}
