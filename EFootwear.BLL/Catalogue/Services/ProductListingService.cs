using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Common;
using EFootwear.Domain.Catalogue;
using EFootwear.Domain.Common;

namespace EFootwear.BLL.Catalogue.Services;

/// <summary>
/// The read screens of the catalogue: View Products (docx 4.2), Search Product (4.3), Check Price (4.5), Products Stock with Sizes On Multiple
/// Stores (4.14) and the Product Report (4.15). A store named in a request must be one the caller may work in (the head office is not a store that
/// holds stock or has its own prices). Row-level security additionally hides the stock of stores the caller may not see.
/// </summary>
public sealed class ProductListingService(IProductListingRepository productListingRepository, ICurrentUser currentUser)
{
    public Task<PagedResult<ProductVariantListItemResponse>> ListVariantRowsAsync(ProductListFilter filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        EnsureStoreIsAllowed(filter.StoreId);
        if (filter.SearchField is not null && !ProductSearchFieldCodes.IsKnown(filter.SearchField))
            throw new DomainException(ErrorKind.Validation, "The search field is not known.");
        if (filter.MatchMode is not null && !ProductMatchModeCodes.IsKnown(filter.MatchMode))
            throw new DomainException(ErrorKind.Validation, "The match mode must be Start, Contains or End.");
        return productListingRepository.ListVariantRowsAsync(filter, pageRequest.Normalized(), cancellationToken);
    }

    /// <summary>The product picker of the screens that work on one product or one size: active sizes found by name, code or barcode, without prices.</summary>
    public async Task<PagedResult<ProductLookupResponse>> LookupVariantsAsync(PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var page = await productListingRepository.ListVariantRowsAsync(new ProductListFilter(OnlyActive: true), pageRequest.Normalized(), cancellationToken);
        var items = page.Items.Select(row => new ProductLookupResponse(row.ProductId, row.VariantId, row.ProductCode, row.ProductName, row.Barcode, row.SizeCode, row.ColorName, row.BrandName)).ToList();
        return new PagedResult<ProductLookupResponse>(items, page.PageNumber, page.PageSize, page.TotalItemCount);
    }

    public async Task<VariantPriceLookupResponse> GetPriceLookupAsync(string? barcode, Guid? storeId, CancellationToken cancellationToken)
    {
        var normalizedBarcode = VariantRules.NormalizeBarcode(barcode);
        if (normalizedBarcode.Length == 0)
            throw new DomainException(ErrorKind.Validation, "Scan or type a barcode.");
        EnsureStoreIsAllowed(storeId);
        return await productListingRepository.FindPriceLookupAsync(normalizedBarcode, storeId, cancellationToken)
               ?? throw new DomainException(ErrorKind.NotFound, $"No product has the barcode {normalizedBarcode}.");
    }

    public Task<PagedResult<ProductStockByStoreRowResponse>> ListStockByStoreAsync(PageRequest pageRequest, CancellationToken cancellationToken) =>
        productListingRepository.ListStockByStoreAsync(pageRequest.Normalized(), cancellationToken);

    public Task<PagedResult<ProductReportRowResponse>> ListProductReportAsync(ProductReportFilter filter, PageRequest pageRequest, CancellationToken cancellationToken) =>
        productListingRepository.ListProductReportAsync(filter, pageRequest.Normalized(), cancellationToken);

    private void EnsureStoreIsAllowed(Guid? storeId)
    {
        if (storeId is not { } id) return;
        if (id == Guid.Empty)
            throw new DomainException(ErrorKind.Validation, "Choose a store.");
        if (!currentUser.CanAccessStore(id))
            throw new DomainException(ErrorKind.Forbidden, "You cannot work in this store.", ErrorCodes.Forbidden);
    }
}
