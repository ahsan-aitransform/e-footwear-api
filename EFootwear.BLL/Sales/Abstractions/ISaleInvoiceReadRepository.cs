using EFootwear.BLL.Common;
using EFootwear.BLL.Sales.Contracts;

namespace EFootwear.BLL.Sales.Abstractions;

/// <summary>
/// Reads of the Sales module: plain SQL over the sales tables, the catalogue and the parties. Row-level security limits every answer to the company and the
/// stores of the caller. Whole rupees everywhere; on a return the amounts are negative, as stored.
/// </summary>
public interface ISaleInvoiceReadRepository
{
    Task<SaleInvoiceResponse?> FindInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken);

    Task<PagedResult<SaleInvoiceListItemResponse>> SearchAsync(SaleInvoiceSearchFilter filter, PageRequest pageRequest, CancellationToken cancellationToken);

    /// <summary>The item with this barcode, priced for the store (stock, shelf price, promotion), or null when no active item has it.</summary>
    Task<SaleItemResponse?> FindItemByBarcodeAsync(Guid storeId, string barcode, CancellationToken cancellationToken);

    /// <summary>Items whose name, code or barcode contains the text (the "Manual" item entry).</summary>
    Task<IReadOnlyList<SaleItemResponse>> SearchItemsAsync(Guid storeId, string searchText, int limit, CancellationToken cancellationToken);

    /// <summary>The posted sale invoice with this number in the store, with the returnable quantity of each line; null when there is none.</summary>
    Task<ReturnableInvoiceResponse?> FindReturnableInvoiceAsync(Guid storeId, long invoiceNumber, CancellationToken cancellationToken);

    /// <summary>The returnable quantity of each named sale line (only lines of posted sale invoices appear).</summary>
    Task<IReadOnlyList<ReturnableSaleLineResponse>> FindReturnableLinesAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<SaleCustomerLookupResponse>> SearchCustomersAsync(string? searchText, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<SaleSalesmanLookupResponse>> ListSalesmenAsync(Guid storeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SalePaymentAccountResponse>> ListPaymentAccountsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SalePosTerminalResponse>> ListPosTerminalsAsync(Guid storeId, CancellationToken cancellationToken);

    Task<SaleStoreCountersResponse> GetStoreCountersAsync(Guid storeId, DateOnly businessDate, CancellationToken cancellationToken);
}
