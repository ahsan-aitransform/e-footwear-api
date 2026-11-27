namespace EFootwear.Domain.Security.Permissions;

/// <summary>
/// SINGLE SOURCE OF TRUTH for managing prices and discounts, which is the company owner's alone. These permissions cannot be given to a user
/// or a role template, and are ignored for anyone but the owner. Looking at prices and discounts (view, print, export) is not managing them.
/// Migration 1503 removed the grants that existed; the database functions of a sale read the same rows, so the till follows.
/// </summary>
public static class OwnerOnlyPermissions
{
    private static readonly string[] PriceAndDiscountScreenPrefixes =
    [
        "PRODUCTS.CHANGE_PRICES_OLD_TO_NEW.", "PRODUCTS.CHANGE_PRICES_BY_PERCENTAGE.", "PRODUCTS.CHANGE_DISCOUNTS.", "PRODUCTS.PRODUCT_DISCOUNTS.",
        "PRODUCTS.PRODUCT_DISCOUNTS_ON_MULTIPLE_STORES.", "PRODUCTS.REMOVE_DISCOUNTS_AND_REVERSE_PRICE.", "PRODUCTS.PRICE_BANDS.",
        "SETTINGS.DEALS.", "SETTINGS.SPECIAL_OFFERS.", "SETTINGS.SPECIAL_OFFER_DETAILS.", "SETTINGS.SALE_ON_STORES.", "SETTINGS.SALE_ON_CATEGORIES.",
        "PROMOTIONS.",
    ];

    /// <summary>A price change or discount on a sale, and the right to change a selling price on a product.</summary>
    private static readonly string[] SinglePermissions = ["PRICE_CHANGE", "DISCOUNT_MANUAL", "DISCOUNT_APPROVE"];
    private static readonly string[] PriceActionSuffixes = [".CHANGE_SELLING_PRICE", ".GIVE_DISCOUNT"];
    private static readonly string[] ReadingActionSuffixes = [".VIEW", ".PRINT", ".EXPORT"];

    public static bool Contains(string permissionCode) =>
        SinglePermissions.Contains(permissionCode, StringComparer.Ordinal)
        || PriceActionSuffixes.Any(suffix => permissionCode.EndsWith(suffix, StringComparison.Ordinal))
        || (PriceAndDiscountScreenPrefixes.Any(prefix => permissionCode.StartsWith(prefix, StringComparison.Ordinal))
            && !ReadingActionSuffixes.Any(suffix => permissionCode.EndsWith(suffix, StringComparison.Ordinal)));
}
