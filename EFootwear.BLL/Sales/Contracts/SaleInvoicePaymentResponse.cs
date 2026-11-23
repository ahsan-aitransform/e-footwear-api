namespace EFootwear.BLL.Sales.Contracts;

public sealed record SaleInvoicePaymentResponse(Guid Id, string PaymentMethodCode, Guid PaymentAccountId, string PaymentAccountTitle, long Amount, string? ReferenceNumber);
