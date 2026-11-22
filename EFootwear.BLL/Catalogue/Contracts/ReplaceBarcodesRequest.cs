namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>Applies a list of barcode replacements in one transaction. Every pair is checked again; one bad pair refuses the whole list.</summary>
public sealed record ReplaceBarcodesRequest(
    IReadOnlyList<BarcodeReplacementPairRequest> Pairs,
    string? Reason,
    Guid? ClientTransactionId);
