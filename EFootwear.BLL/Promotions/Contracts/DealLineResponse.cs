namespace EFootwear.BLL.Promotions.Contracts;

/// <summary>
/// One product of a deal (docx 2.12 grid): its quantity, the current sale price of the size and the net price the deal sells it at. The discount is the difference, so it
/// follows a later change of the sale price; only the net price is stored.
/// </summary>
public sealed record DealLineResponse(
    Guid VariantId,
    string Barcode,
    string ProductCode,
    string ProductName,
    string SizeCode,
    decimal Quantity,
    long SalePrice,
    long NetUnitPrice,
    long Amount);
