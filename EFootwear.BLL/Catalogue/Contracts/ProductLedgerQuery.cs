namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>
/// Product Ledger (docx 4.4): the stock movements of one product in one store between two dates. The product is chosen by a scanned barcode
/// (any size) or by ProductId. Detail = every movement; without it only the totals by movement type.
/// </summary>
public sealed record ProductLedgerQuery(
    Guid StoreId,
    string? Barcode,
    Guid? ProductId,
    DateOnly FromDate,
    DateOnly ToDate,
    bool Detail);
