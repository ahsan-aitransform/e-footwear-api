namespace EFootwear.BLL.Sales.Contracts;

public sealed record VoidAndReenterSaleResponse(SaleInvoiceResponse VoidedInvoice, SaleInvoiceResponse CorrectedInvoice);
