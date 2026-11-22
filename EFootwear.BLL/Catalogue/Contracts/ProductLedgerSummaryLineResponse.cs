namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>Totals of one movement type (sale, purchase, return, transfer ...) in the period of the Product Ledger.</summary>
public sealed record ProductLedgerSummaryLineResponse(
    string DocumentType,
    decimal QuantityIn,
    decimal QuantityOut,
    int MovementCount);
