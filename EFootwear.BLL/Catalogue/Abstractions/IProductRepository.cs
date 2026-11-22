using EFootwear.BLL.Catalogue.Contracts;

namespace EFootwear.BLL.Catalogue.Abstractions;

/// <summary>
/// Writes of the catalogue core: products, sizes (variants) and their company list prices (catalog.products, catalog.variants, catalog.variant_prices).
/// Implemented in EFootwear.DAL/Catalogue/Repositories/ProductRepository.cs. Nothing is committed here: the service commits the unit of work.
/// </summary>
public interface IProductRepository
{
    Task<ProductDetailResponse?> FindDetailAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>The code the next product will get (the database allocates the real one at save time).</summary>
    Task<string> PeekNextProductCodeAsync(CancellationToken cancellationToken);

    /// <summary>Allocates the next company-wide product code and inserts the product.</summary>
    Task<(Guid ProductId, string Code)> InsertProductAsync(ProductHeaderData header, Guid createdBy, CancellationToken cancellationToken);

    /// <summary>Updates the header; throws a concurrency error when the row version is stale.</summary>
    Task UpdateProductAsync(Guid productId, long rowVersion, ProductHeaderData header, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExistingVariantSnapshot>> ListVariantsAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>Inserts the size and its list prices.</summary>
    Task<Guid> InsertVariantAsync(Guid productId, ProductVariantWriteData data, Guid createdBy, CancellationToken cancellationToken);

    /// <summary>Updates colour, size group, size name, barcode and status of a size (not its prices).</summary>
    Task UpdateVariantAsync(Guid variantId, ProductVariantWriteData data, CancellationToken cancellationToken);

    Task UpdateVariantPricesAsync(Guid variantId, long purchasePrice, long salePrice, CancellationToken cancellationToken);
    Task SoftDeleteVariantAsync(Guid variantId, Guid deletedBy, CancellationToken cancellationToken);
    Task SoftDeleteProductAsync(Guid productId, Guid deletedBy, CancellationToken cancellationToken);

    /// <summary>Of these barcodes, the ones that are already used by a size (other than the excepted ones) or by a deal bundle.</summary>
    Task<IReadOnlyList<string>> FindBarcodesInUseAsync(IReadOnlyCollection<string> barcodes, IReadOnlyCollection<Guid> exceptVariantIds,
                                                       CancellationToken cancellationToken);
}
