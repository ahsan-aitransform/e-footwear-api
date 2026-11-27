using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.Domain.Catalogue;
using EFootwear.Domain.Common;

namespace EFootwear.BLL.Catalogue.Services;

/// <summary>
/// Product Ledger (docx 4.4): the stock movements of one product in one store for a date range, read from the append-only stock ledger.
/// Opening + in - out = closing. The product is found from a scanned barcode of any of its sizes, or from its id.
/// </summary>
public sealed class ProductLedgerService(
    IProductLedgerRepository productLedgerRepository,
    IProductListingRepository productListingRepository,
    ICurrentUser currentUser)
{
    /// <summary>A sanity limit, not a business rule: a ledger over many years is a report, not a screen.</summary>
    public const int MaximumDaysInRange = 3660;

    public async Task<ProductLedgerResponse> GetLedgerAsync(ProductLedgerQuery query, CancellationToken cancellationToken)
    {
        if (query.StoreId == Guid.Empty)
            throw new DomainException(ErrorKind.Validation, "Choose a store.");
        if (!currentUser.CanAccessStore(query.StoreId))
            throw new DomainException(ErrorKind.Forbidden, "You cannot work in this store.", ErrorCodes.Forbidden);
        if (query.ToDate < query.FromDate)
            throw new DomainException(ErrorKind.Validation, "The end date cannot be before the start date.");
        if (query.ToDate.DayNumber - query.FromDate.DayNumber > MaximumDaysInRange)
            throw new DomainException(ErrorKind.Validation, $"Choose at most {MaximumDaysInRange} days.");

        var productId = query.ProductId;
        if (productId is null)
        {
            var barcode = VariantRules.NormalizeBarcode(query.Barcode);
            if (barcode.Length == 0)
                throw new DomainException(ErrorKind.Validation, "Scan a barcode or choose a product.");
            productId = await productListingRepository.FindProductIdByBarcodeAsync(barcode, cancellationToken)
                        ?? throw new DomainException(ErrorKind.NotFound, $"No product has the barcode or product code {barcode}.");
        }

        return await productLedgerRepository.GetLedgerAsync(productId.Value, query.StoreId, query.FromDate, query.ToDate, query.Detail, cancellationToken)
               ?? throw new DomainException(ErrorKind.NotFound, "The product was not found.");
    }
}
