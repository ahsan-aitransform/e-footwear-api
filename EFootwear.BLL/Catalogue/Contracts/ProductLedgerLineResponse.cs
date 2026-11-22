namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>One stock movement (inv.stock_ledger). QuantityAfter is the stock of that size right after the movement.</summary>
public sealed record ProductLedgerLineResponse(
    DateOnly TransactionDate,
    DateTimeOffset TransactionAt,
    string SizeCode,
    string Barcode,
    string DocumentType,
    Guid DocumentId,
    decimal QuantityIn,
    decimal QuantityOut,
    decimal QuantityAfter,
    decimal UnitCost);
