using EFootwear.BLL.Sales.Contracts;

namespace EFootwear.BLL.Sales.Abstractions;

/// <summary>
/// Writes of the Sales module. Every method calls one database function (sales.post_sale_invoice, sales.post_return_invoice, sales.post_exchange,
/// sales.void_invoice, migration 0500): the application never inserts invoice, line, payment or stock rows itself. The functions check the permissions again,
/// number the document, move the stock and let the baseline triggers post the journal when the transaction commits.
/// </summary>
public interface ISaleInvoicePostingRepository
{
    /// <summary>Posts a sale. With <paramref name="replacedInvoiceId"/> the old invoice is voided first (void and re-enter). A repeated client transaction id returns the first invoice.</summary>
    Task<Guid> PostSaleAsync(CreateSaleInvoiceRequest request, Guid? replacedInvoiceId, string? replaceReason, CancellationToken cancellationToken);

    Task<Guid> PostReturnAsync(CreateSaleReturnRequest request, CancellationToken cancellationToken);

    Task<(Guid ReturnInvoiceId, Guid SaleInvoiceId)> PostExchangeAsync(CreateExchangeRequest request, CancellationToken cancellationToken);

    /// <summary>Voids an invoice (and the other half of its exchange). Returns how many invoices were voided.</summary>
    Task<int> VoidAsync(Guid invoiceId, string reason, CancellationToken cancellationToken);
}
