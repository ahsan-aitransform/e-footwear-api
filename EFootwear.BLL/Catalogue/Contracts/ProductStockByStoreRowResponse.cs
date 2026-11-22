namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>A row of Products Stock with Sizes On Multiple Stores (docx 4.14): one size of a product in one store. Only stores the caller may see appear.</summary>
public sealed record ProductStockByStoreRowResponse(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    Guid StoreId,
    string StoreName,
    string SizeCode,
    string Barcode,
    decimal StockQuantity);
