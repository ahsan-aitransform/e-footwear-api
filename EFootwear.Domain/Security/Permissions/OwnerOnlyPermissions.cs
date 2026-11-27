namespace EFootwear.Domain.Security.Permissions;

/// <summary>
/// SINGLE SOURCE OF TRUTH for what is the company owner's alone. These permissions cannot be given to a user or a role template, and are
/// ignored for anyone but the owner:
///   * the screens that manage prices, discounts and promotions, and Products Stock with Sizes on Multiple Stores: nobody else may even open them;
///   * changing price bands (reading them stays open, the commission rules choose from them);
///   * a price change or discount on a sale, and changing a selling price on a product.
/// Migrations 1503 to 1505 removed the grants that existed; the database functions of a sale read the same rows, so the till follows.
/// </summary>
public static class OwnerOnlyPermissions
{
    /// <summary>Screens nobody but the owner may even open.</summary>
    private static readonly string[] ClosedScreenPrefixes =
    [
        "PRODUCTS.CHANGE_PRICES_OLD_TO_NEW.", "PRODUCTS.CHANGE_PRICES_BY_PERCENTAGE.", "PRODUCTS.CHANGE_DISCOUNTS.", "PRODUCTS.PRODUCT_DISCOUNTS.",
        "PRODUCTS.PRODUCT_DISCOUNTS_ON_MULTIPLE_STORES.", "PRODUCTS.REMOVE_DISCOUNTS_AND_REVERSE_PRICE.", "PRODUCTS.PRODUCTS_STOCK_WITH_SIZES_ON_MULTIPLE_STORES.",
        "SETTINGS.DEALS.", "SETTINGS.SPECIAL_OFFERS.", "SETTINGS.SPECIAL_OFFER_DETAILS.", "SETTINGS.SALE_ON_STORES.", "SETTINGS.SALE_ON_CATEGORIES.",
        "PROMOTIONS.",
    ];

    /// <summary>Screens others may read (view, print, export) but not change.</summary>
    private static readonly string[] ReadOnlyForOthersPrefixes = ["PRODUCTS.PRICE_BANDS."];

    private static readonly string[] SinglePermissions = ["PRICE_CHANGE", "DISCOUNT_MANUAL", "DISCOUNT_APPROVE"];
    private static readonly string[] PriceActionSuffixes = [".CHANGE_SELLING_PRICE", ".GIVE_DISCOUNT"];
    private static readonly string[] ReadingActionSuffixes = [".VIEW", ".PRINT", ".EXPORT"];

    public static bool Contains(string permissionCode) =>
        ClosedScreenPrefixes.Any(prefix => permissionCode.StartsWith(prefix, StringComparison.Ordinal))
        || SinglePermissions.Contains(permissionCode, StringComparer.Ordinal)
        || PriceActionSuffixes.Any(suffix => permissionCode.EndsWith(suffix, StringComparison.Ordinal))
        || (ReadOnlyForOthersPrefixes.Any(prefix => permissionCode.StartsWith(prefix, StringComparison.Ordinal))
            && !ReadingActionSuffixes.Any(suffix => permissionCode.EndsWith(suffix, StringComparison.Ordinal)));
}
