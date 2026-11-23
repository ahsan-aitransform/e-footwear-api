namespace EFootwear.BLL.Sales.Contracts;

/// <summary>"Delete" of the docx: the invoice is voided with a reason. Nothing is deleted; the journal, the stock and the commission are reversed.</summary>
public sealed record VoidSaleInvoiceRequest(string Reason);
