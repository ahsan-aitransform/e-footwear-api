namespace EFootwear.Domain.Catalogue;

/// <summary>
/// Values of the promotion rule tables that the product discount screens (docx 4.9 to 4.12) write. A product discount is a promo.rules row,
/// not a column on the variant: the pricing engine turns published rules into promo.effective_prices, which is what the POS reads.
/// </summary>
public static class ProductDiscountRuleCodes
{
    /// <summary>promo.rules.created_from of rules made by the product discount screens (migration 0300).</summary>
    public const string CreatedFromProductDiscount = "PRODUCT_DISCOUNT";

    public const string DiscountTypePercent = "PERCENT";
    /// <summary>Whole rupees off each piece.</summary>
    public const string DiscountTypeAmount = "AMOUNT";
    public const string TargetTypeProduct = "PRODUCT";
    public const string StatusPublished = "PUBLISHED";
    public const string StatusCancelled = "CANCELLED";

    /// <summary>integ.channels.kind of the in-store point of sale channel; store-scoped discounts must name it.</summary>
    public const string StorePointOfSaleChannelKind = "STORE_POS";
}
