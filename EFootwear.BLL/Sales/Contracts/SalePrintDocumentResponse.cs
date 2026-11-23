namespace EFootwear.BLL.Sales.Contracts;

/// <summary>Everything a printed invoice, return receipt or gate pass needs: the business header, the paper defaults and the invoice itself.</summary>
public sealed record SalePrintDocumentResponse(SaleBusinessHeaderResponse BusinessHeader, SalePrintDefaultsResponse PrintDefaults, SaleInvoiceResponse Invoice);
