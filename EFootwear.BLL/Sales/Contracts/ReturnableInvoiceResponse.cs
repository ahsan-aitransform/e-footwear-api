namespace EFootwear.BLL.Sales.Contracts;

/// <summary>A posted sale invoice found by its number, with the lines the customer may bring back.</summary>
public sealed record ReturnableInvoiceResponse(
    Guid InvoiceId,
    long InvoiceNumber,
    Guid StoreId,
    DateOnly SaleDate,
    Guid? CustomerId,
    string? CustomerName,
    string? WalkInCustomerName,
    IReadOnlyList<ReturnableSaleLineResponse> Lines);
