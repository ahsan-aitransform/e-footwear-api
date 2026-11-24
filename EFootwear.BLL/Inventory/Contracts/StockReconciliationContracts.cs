namespace EFootwear.BLL.Inventory.Contracts;

/// <summary>A size where the stock balance does not equal the sum of its stock ledger. It should never happen; it is shown so it can be investigated, never fixed silently.</summary>
public sealed record StockReconciliationMismatchResponse(
    Guid StoreId,
    string StoreName,
    Guid VariantId,
    string Barcode,
    string ProductName,
    string SizeCode,
    decimal BalanceQuantity,
    decimal LedgerQuantity,
    decimal DifferenceQuantity);

/// <summary>Stock Reconciliation: balance against ledger for every size of the store(s), plus the pieces on their way between stores and the books' stock value.</summary>
public sealed record StockReconciliationResponse(
    DateTimeOffset CheckedAt,
    Guid? StoreId,
    int CheckedSizeCount,
    int NegativeBalanceCount,
    IReadOnlyList<StockReconciliationMismatchResponse> Mismatches,
    int TransfersInTransitCount,
    decimal PiecesInTransitQuantity,
    int OpenLossCaseCount,
    long StockValueAmount,
    bool IsClean);
