namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>The grid of Replace Barcodes (docx 4.13) after an import: every row checked, nothing saved yet.</summary>
public sealed record BarcodeReplacementPreviewResponse(
    IReadOnlyList<BarcodeReplacementRowResponse> Rows,
    int ValidRowCount,
    int InvalidRowCount);
