namespace EFootwear.BLL.Sales.Contracts;

/// <summary>
/// One stored line. On a return the quantity and the amounts are negative (the sign convention of sales.invoice_lines).
/// <paramref name="DiscountPerUnit"/> is shelf price minus charged price, so it includes a promotion and a price change as well as a manual discount.
/// </summary>
public sealed record SaleInvoiceLineResponse(
    Guid LineId,
    int LineNumber,
    Guid VariantId,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string? ProductNameUrdu,
    string? UnitName,
    string SizeCode,
    string Barcode,
    decimal Quantity,
    long ListPrice,
    long UnitPrice,
    long DiscountPerUnit,
    decimal DiscountPercent,
    long DiscountAmount,
    long NetAmount,
    string DiscountSourceCode,
    Guid? OriginalLineId,
    long? OriginalInvoiceNumber,
    string? ReturnReason,
    string? ItemConditionCode);
