namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>One old-to-new barcode pair of Replace Barcodes (docx 4.13).</summary>
public sealed record BarcodeReplacementPairRequest(string OldBarcode, string NewBarcode);
