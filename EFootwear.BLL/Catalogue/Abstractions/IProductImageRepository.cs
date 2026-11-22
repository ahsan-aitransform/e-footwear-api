namespace EFootwear.BLL.Catalogue.Abstractions;

/// <summary>The four picture slots of a product (catalog.product_images). The file is kept by IFileStorage; this keeps the slot and the storage key.</summary>
public interface IProductImageRepository
{
    Task<string?> FindStorageKeyAsync(Guid productId, int slot, CancellationToken cancellationToken);

    /// <summary>Puts the storage key in the slot (the slot is replaced when it was filled).</summary>
    Task SetSlotAsync(Guid productId, int slot, string storageKey, CancellationToken cancellationToken);

    Task ClearSlotAsync(Guid productId, int slot, CancellationToken cancellationToken);
}
