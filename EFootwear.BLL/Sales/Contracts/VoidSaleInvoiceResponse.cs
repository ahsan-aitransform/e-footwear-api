namespace EFootwear.BLL.Sales.Contracts;

/// <summary>The invoice that was voided. <paramref name="VoidedInvoiceCount"/> is 2 when it was one half of an exchange: both halves are voided together.</summary>
public sealed record VoidSaleInvoiceResponse(SaleInvoiceResponse Invoice, int VoidedInvoiceCount);
