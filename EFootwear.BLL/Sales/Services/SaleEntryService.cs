using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Catalogue.Services;
using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Services;
using EFootwear.BLL.Sales.Abstractions;
using EFootwear.BLL.Sales.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Sales;

namespace EFootwear.BLL.Sales.Services;

/// <summary>
/// What the New Sale, Sale Return and Exchange screens read while the cashier works: the numbers and totals of the day, items by barcode or name, customers,
/// salesmen, payment accounts and POS terminals (docx 5.1 to 5.3). Read-only: nothing here posts anything. The store always comes from a request that is
/// checked against the stores of the caller.
/// </summary>
public sealed class SaleEntryService(
    ISaleInvoiceReadRepository readRepository,
    SalePrintContextService printContextService,
    CompanySettingsService companySettingsService,
    ProductImageService productImageService,
    ICurrentUser currentUser)
{
    public async Task<SaleEntryContextResponse> GetEntryContextAsync(Guid storeId, DateOnly? businessDate, CancellationToken cancellationToken)
    {
        EnsureStoreAccess(storeId);
        var date = businessDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var counters = await readRepository.GetStoreCountersAsync(storeId, date, cancellationToken);

        var paymentMethods = (await companySettingsService.GetEnabledPaymentMethodsAsync(cancellationToken))
            .Select(method => new SalePaymentMethodResponse(method.Code, method.DisplayNameEnglish, method.DisplayNameUrdu))
            .ToList();
        var promotionPolicy = (await companySettingsService.GetPromotionPolicyAsync(cancellationToken)).Values;

        return new SaleEntryContextResponse(
            storeId, counters.StoreName, date, counters.NextInvoiceNumber, counters.NextDailyBillNumber, counters.NextReturnInvoiceNumber,
            counters.TodaySaleAmount, counters.RoundingModeCode, promotionPolicy.MaximumManualDiscountPercentage, paymentMethods,
            await printContextService.BuildBusinessHeaderAsync(storeId, counters.StoreName, cancellationToken),
            await printContextService.BuildPrintDefaultsAsync(storeId, cancellationToken));
    }

    public async Task<SaleItemResponse> FindItemByBarcodeAsync(Guid storeId, string? barcode, CancellationToken cancellationToken)
    {
        EnsureStoreAccess(storeId);
        var cleanedBarcode = barcode?.Trim();
        if (string.IsNullOrEmpty(cleanedBarcode))
            throw new DomainException(ErrorKind.Validation, "Scan or type a barcode.");
        return await readRepository.FindItemByBarcodeAsync(storeId, cleanedBarcode, cancellationToken)
               ?? throw new DomainException(ErrorKind.NotFound, "No active item has this barcode.");
    }

    public async Task<IReadOnlyList<SaleItemResponse>> SearchItemsAsync(Guid storeId, string? searchText, CancellationToken cancellationToken)
    {
        EnsureStoreAccess(storeId);
        var cleanedText = searchText?.Trim();
        if (string.IsNullOrEmpty(cleanedText)) return [];
        return await readRepository.SearchItemsAsync(storeId, cleanedText, SaleRules.SearchResultLimit, cancellationToken);
    }

    public Task<IReadOnlyList<SaleCustomerLookupResponse>> SearchCustomersAsync(string? searchText, CancellationToken cancellationToken) =>
        readRepository.SearchCustomersAsync(string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim(), SaleRules.SearchResultLimit, cancellationToken);

    /// <summary>What the customer bought before, in the stores the caller can see.</summary>
    public Task<PagedResult<SaleCustomerPurchaseResponse>> ListCustomerPurchasesAsync(Guid customerId, PageRequest pageRequest, CancellationToken cancellationToken) =>
        readRepository.ListCustomerPurchasesAsync(customerId, pageRequest.Normalized(), cancellationToken);

    public Task<IReadOnlyList<SaleSalesmanLookupResponse>> ListSalesmenAsync(Guid storeId, CancellationToken cancellationToken)
    {
        EnsureStoreAccess(storeId);
        return readRepository.ListSalesmenAsync(storeId, cancellationToken);
    }

    public Task<IReadOnlyList<SalePaymentAccountResponse>> ListPaymentAccountsAsync(CancellationToken cancellationToken) =>
        readRepository.ListPaymentAccountsAsync(cancellationToken);

    public Task<IReadOnlyList<SalePosTerminalResponse>> ListPosTerminalsAsync(Guid storeId, CancellationToken cancellationToken)
    {
        EnsureStoreAccess(storeId);
        return readRepository.ListPosTerminalsAsync(storeId, cancellationToken);
    }

    /// <summary>The first picture of a product, for the picture box of the sale screens. Null when the product has none.</summary>
    public Task<ProductImageContent?> OpenItemPictureAsync(Guid productId, CancellationToken cancellationToken) =>
        productImageService.OpenAsync(productId, slot: 1, cancellationToken);

    private void EnsureStoreAccess(Guid storeId)
    {
        if (storeId == Guid.Empty || !currentUser.CanAccessStore(storeId))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to this store.");
    }
}
