namespace EFootwear.BLL.Sales.Contracts;

public sealed record SaleInvoiceListItemResponse(
    Guid Id,
    Guid StoreId,
    string StoreName,
    string DocumentTypeCode,
    long InvoiceNumber,
    int? DailyBillNumber,
    DateOnly SaleDate,
    string? CustomerName,
    string? WalkInCustomerName,
    string? SalesmanName,
    long NetAmount,
    long PaidAmount,
    long OutstandingAmount,
    string StatusCode,
    bool IsPartOfExchange,
    string? VoidReason,
    DateTimeOffset? VoidedAt,
    string? VoidedByName);
