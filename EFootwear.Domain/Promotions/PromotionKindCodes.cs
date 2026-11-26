namespace EFootwear.Domain.Promotions;

/// <summary>
/// Which screen made a promotion rule (promo.rules.created_from, migration 0901). Each screen lists and changes only its own kind, so the Sale on Stores screen can
/// never end an offer, and none of them touches the discounts of the product screens (PRODUCT_DISCOUNT) or an ordinary promotion (PROMOTION).
/// </summary>
public static class PromotionKindCodes
{
    public const string SpecialOffer = "SPECIAL_OFFER";
    public const string SaleOnStores = "SALE_ON_STORES";
    public const string SaleOnCategories = "SALE_ON_CATEGORIES";

    public static readonly IReadOnlyList<string> All = [SpecialOffer, SaleOnStores, SaleOnCategories];

    public static bool IsValid(string? kindCode) => kindCode is SpecialOffer or SaleOnStores or SaleOnCategories;
}
