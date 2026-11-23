namespace EFootwear.BLL.Sales.Contracts;

public sealed record ExchangeResponse(SaleInvoiceResponse ReturnInvoice, SaleInvoiceResponse SaleInvoice);
