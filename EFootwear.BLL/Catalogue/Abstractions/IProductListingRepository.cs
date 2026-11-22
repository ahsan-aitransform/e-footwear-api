using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Common;

namespace EFootwear.BLL.Catalogue.Abstractions;

/// <summary>
/// Read models of the catalogue screens (View/Search Products, Check Price, stock on several stores, Product Report). Implemented in
/// EFootwear.DAL/Catalogue/Repositories/ProductListingRepository.cs with set-based SQL; row-level security limits stock to the caller's stores.
/// </summary>
public interface IProductListingRepository
{
    Task<PagedResult<ProductVariantListItemResponse>> ListVariantRowsAsync(ProductListFilter filter, PageRequest pageRequest, CancellationToken cancellationToken);
    Task<VariantPriceLookupResponse?> FindPriceLookupAsync(string barcode, Guid? storeId, CancellationToken cancellationToken);
    Task<Guid?> FindProductIdByBarcodeAsync(string barcode, CancellationToken cancellationToken);
    Task<PagedResult<ProductStockByStoreRowResponse>> ListStockByStoreAsync(PageRequest pageRequest, CancellationToken cancellationToken);
    Task<PagedResult<ProductReportRowResponse>> ListProductReportAsync(ProductReportFilter filter, PageRequest pageRequest, CancellationToken cancellationToken);
    Task<BarcodeLabelData?> FindLabelDataAsync(Guid variantId, Guid? storeId, CancellationToken cancellationToken);
}
