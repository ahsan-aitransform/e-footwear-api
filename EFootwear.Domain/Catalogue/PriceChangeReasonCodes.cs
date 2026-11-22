namespace EFootwear.Domain.Catalogue;

/// <summary>The reason text written to catalog.price_events.reason, so the price history says which screen changed a price.</summary>
public static class PriceChangeReasonCodes
{
    public const string InitialPrice = "INITIAL_PRICE";
    public const string ProductEdit = "PRODUCT_EDIT";
    public const string BulkOldToNew = "BULK_OLD_TO_NEW";
    public const string BulkPercentage = "BULK_PERCENTAGE";
    public const string NewSalePrice = "PRODUCT_NEW_SALE_PRICE";
    public const string PriceReversal = "PRICE_REVERSAL";
}
