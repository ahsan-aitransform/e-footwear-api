namespace EFootwear.Domain.Inventory;

/// <summary>The document number series of core.number_series used by the inventory documents (one counter per store). They match the names used inside the database functions.</summary>
public static class InventoryNumberSeriesCodes
{
    public const string StockAdjustment = "STOCK_ADJUSTMENT";
    public const string StockAudit = "STOCK_AUDIT";
    public const string ProductClaim = "PRODUCT_CLAIM";
    public const string StockTransfer = "STOCK_TRANSFER";
}
