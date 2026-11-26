namespace EFootwear.BLL.Sales.Contracts;

/// <summary>
/// One earlier posted invoice of a customer, with its items, for the purchase history of the New Sale screen. On a return the quantities and
/// the amounts are negative, as stored.
/// </summary>
public sealed record SaleCustomerPurchaseResponse(
    Guid InvoiceId,
    string StoreName,
    string DocumentTypeCode,
    long InvoiceNumber,
    DateOnly SaleDate,
    long NetAmount,
    long PaidAmount,
    long OutstandingAmount,
    IReadOnlyList<SaleCustomerPurchaseLineResponse> Lines);
