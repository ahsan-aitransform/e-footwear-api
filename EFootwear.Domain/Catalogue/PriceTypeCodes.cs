namespace EFootwear.Domain.Catalogue;

/// <summary>Which price a price event or a bulk change is about (catalog.price_events.price_type).</summary>
public static class PriceTypeCodes
{
    public const string Purchase = "PURCHASE";
    public const string Sale = "SALE";

    public static bool IsKnown(string? priceTypeCode) => priceTypeCode is Purchase or Sale;
}
