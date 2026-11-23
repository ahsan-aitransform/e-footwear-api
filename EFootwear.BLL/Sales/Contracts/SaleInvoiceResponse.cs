namespace EFootwear.BLL.Sales.Contracts;

/// <summary>
/// A stored sale or return invoice with its lines and payments (the invoice detail, the answer to a save, and the body of a printed receipt).
/// Amounts are whole rupees; on a RETURN they are negative. <paramref name="OutstandingAmount"/> is the part of a sale put on the account of the customer.
/// </summary>
public sealed record SaleInvoiceResponse(
    Guid Id,
    Guid StoreId,
    string StoreName,
    string DocumentTypeCode,
    long InvoiceNumber,
    int? DailyBillNumber,
    DateOnly SaleDate,
    DateTimeOffset OccurredAt,
    Guid? CustomerPartyId,
    string? CustomerName,
    string? WalkInCustomerName,
    string? WalkInCustomerMobile,
    Guid? SalesmanPartyId,
    string? SalesmanName,
    string? PosTerminalLabel,
    string? Description,
    long GrossAmount,
    long DiscountAmount,
    long TaxAmount,
    long NetAmount,
    long PaidAmount,
    long CashReturnedAmount,
    long OutstandingAmount,
    string StatusCode,
    string? VoidReason,
    DateTimeOffset? VoidedAt,
    string? VoidedByName,
    string? CreatedByName,
    Guid? OriginalInvoiceId,
    long? OriginalInvoiceNumber,
    Guid? RelatedInvoiceId,
    long? RelatedInvoiceNumber,
    Guid? ReplacesInvoiceId,
    long? ReplacesInvoiceNumber,
    long RowVersion,
    IReadOnlyList<SaleInvoiceLineResponse> Lines,
    IReadOnlyList<SaleInvoicePaymentResponse> Payments);
