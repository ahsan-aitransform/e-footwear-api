namespace EFootwear.BLL.Sales.Contracts;

/// <summary>
/// "Edit" of a posted sale (Part 11): the old invoice is voided with a reason and the corrected sale is posted in the same transaction, so a posted
/// invoice is never changed in place and the original stays in the history.
/// </summary>
public sealed record VoidAndReenterSaleRequest(string VoidReason, CreateSaleInvoiceRequest CorrectedSale);
