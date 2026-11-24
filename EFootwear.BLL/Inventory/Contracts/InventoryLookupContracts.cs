namespace EFootwear.BLL.Inventory.Contracts;

/// <summary>One article size found by barcode or by search, with the stock of the chosen store (the entry rows of Stock Adjustment, Claim, Transfer and Audit).</summary>
public sealed record InventoryItemResponse(
    Guid VariantId,
    string ProductCode,
    string ProductName,
    string? ProductNameUrdu,
    string? UnitName,
    string SizeCode,
    string? ColorName,
    string Barcode,
    decimal StockQuantity,
    long PurchasePriceAmount);

/// <summary>A row of the non-zero stock list (docx 5.8): what the books say is in the store.</summary>
public sealed record NonZeroStockRowResponse(
    Guid VariantId,
    string Barcode,
    string ProductName,
    string SizeCode,
    long PurchasePriceAmount,
    decimal StockQuantity);

/// <summary>A store for the pickers of the inventory screens (the destination of a transfer is any active store of the company).</summary>
public sealed record InventoryStoreResponse(Guid Id, string Code, string Name, bool IsUserStore);
