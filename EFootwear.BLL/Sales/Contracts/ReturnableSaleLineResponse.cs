namespace EFootwear.BLL.Sales.Contracts;

/// <summary>A line of a sale that can be returned, with how much of it was sold, already returned (voided returns do not count) and is still returnable.</summary>
public sealed record ReturnableSaleLineResponse(
    Guid LineId,
    int LineNumber,
    Guid InvoiceId,
    long InvoiceNumber,
    Guid VariantId,
    string ProductCode,
    string ProductName,
    string? ProductNameUrdu,
    string? UnitName,
    string SizeCode,
    string Barcode,
    decimal SoldQuantity,
    decimal AlreadyReturnedQuantity,
    decimal ReturnableQuantity,
    long ListPrice,
    long UnitPrice,
    long DiscountPerUnit,
    decimal DiscountPercent);
