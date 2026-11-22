namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>
/// The Product Ledger of one product in one store. Opening and closing are the stock of all sizes before the first day and after the last day;
/// opening + in - out = closing. Lines are empty unless the detail version was asked for.
/// </summary>
public sealed record ProductLedgerResponse(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    Guid StoreId,
    string StoreName,
    DateOnly FromDate,
    DateOnly ToDate,
    decimal OpeningQuantity,
    decimal ClosingQuantity,
    IReadOnlyList<ProductLedgerSummaryLineResponse> Summary,
    IReadOnlyList<ProductLedgerLineResponse> Lines);
