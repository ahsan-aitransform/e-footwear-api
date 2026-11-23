using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Sales.Abstractions;
using EFootwear.BLL.Sales.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Sales;

namespace EFootwear.BLL.Sales.Services;

/// <summary>
/// View Invoices, View Deleted Invoices, the invoice detail and the print document (docx 5.13 and the designed View Invoices page). Read-only. A voided
/// invoice is still an invoice: View Invoices lists it with its status, and View Deleted Invoices lists only the voided ones with who, when and why.
/// </summary>
public sealed class SaleInvoiceQueryService(
    ISaleInvoiceReadRepository readRepository,
    SalePrintContextService printContextService,
    ICurrentUser currentUser)
{
    public Task<PagedResult<SaleInvoiceListItemResponse>> SearchAsync(SaleInvoiceSearchFilter filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        EnsureFilterIsValid(filter);
        return readRepository.SearchAsync(filter, pageRequest.Normalized(), cancellationToken);
    }

    public Task<PagedResult<SaleInvoiceListItemResponse>> SearchDeletedAsync(SaleInvoiceSearchFilter filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var voidOnlyFilter = filter with { StatusCode = SaleInvoiceStatusCodes.Void };
        EnsureFilterIsValid(voidOnlyFilter);
        return readRepository.SearchAsync(voidOnlyFilter, pageRequest.Normalized(), cancellationToken);
    }

    public async Task<SaleInvoiceResponse> GetAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await readRepository.FindInvoiceAsync(invoiceId, cancellationToken)
                      ?? throw new DomainException(ErrorKind.NotFound, "The invoice was not found.");
        EnsureStoreAccess(invoice.StoreId);
        return invoice;
    }

    /// <summary>A voided invoice for View Deleted Invoices. A posted invoice is not found here: it is not "deleted".</summary>
    public async Task<SaleInvoiceResponse> GetDeletedAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await GetAsync(invoiceId, cancellationToken);
        if (invoice.StatusCode != SaleInvoiceStatusCodes.Void)
            throw new DomainException(ErrorKind.NotFound, "This invoice has not been voided.");
        return invoice;
    }

    public async Task<SalePrintDocumentResponse> GetPrintDocumentAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await GetAsync(invoiceId, cancellationToken);
        return new SalePrintDocumentResponse(
            await printContextService.BuildBusinessHeaderAsync(invoice.StoreId, invoice.StoreName, cancellationToken),
            await printContextService.BuildPrintDefaultsAsync(invoice.StoreId, cancellationToken),
            invoice);
    }

    /// <summary>A posted sale found by its number in a store, with the lines that can still be returned (Sale Return and Exchange).</summary>
    public async Task<ReturnableInvoiceResponse> FindReturnableInvoiceAsync(Guid storeId, long invoiceNumber, CancellationToken cancellationToken)
    {
        EnsureStoreAccess(storeId);
        if (invoiceNumber <= 0)
            throw new DomainException(ErrorKind.Validation, "Enter the invoice number.");
        return await readRepository.FindReturnableInvoiceAsync(storeId, invoiceNumber, cancellationToken)
               ?? throw new DomainException(ErrorKind.NotFound, "No posted sale invoice has this number in this store.");
    }

    private void EnsureFilterIsValid(SaleInvoiceSearchFilter filter)
    {
        if (filter.DocumentTypeCode is not null && !SaleDocumentTypeCodes.IsValid(filter.DocumentTypeCode))
            throw new DomainException(ErrorKind.Validation, "That document type does not exist.");
        if (filter.StatusCode is not null && !SaleInvoiceStatusCodes.IsValid(filter.StatusCode))
            throw new DomainException(ErrorKind.Validation, "That invoice status does not exist.");
        if (filter.StoreId is { } storeId) EnsureStoreAccess(storeId);
        if (filter.FromDate is { } from && filter.ToDate is { } to && from > to)
            throw new DomainException(ErrorKind.Validation, "The From date cannot be after the To date.");
        if (filter.MinimumNetAmount is { } minimum && filter.MaximumNetAmount is { } maximum && minimum > maximum)
            throw new DomainException(ErrorKind.Validation, "The smallest amount cannot be above the largest amount.");
    }

    private void EnsureStoreAccess(Guid storeId)
    {
        if (!currentUser.CanAccessStore(storeId))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to this store.");
    }
}
