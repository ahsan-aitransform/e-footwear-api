using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Catalogue;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Catalogue.Repositories;

/// <summary>The four picture slots of a product (catalog.product_images, unique per product and slot).</summary>
public sealed class ProductImageRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IProductImageRepository
{
    public async Task<string?> FindStorageKeyAsync(Guid productId, int slot, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.ProductImages.AsNoTracking()
            .Where(image => image.CompanyId == companyId && image.ProductId == productId && image.Slot == slot)
            .Select(image => image.StorageKey).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task SetSlotAsync(Guid productId, int slot, string storageKey, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var existing = await databaseContext.ProductImages
            .SingleOrDefaultAsync(image => image.CompanyId == companyId && image.ProductId == productId && image.Slot == slot, cancellationToken);
        if (existing is null)
            databaseContext.ProductImages.Add(new ProductImage { CompanyId = companyId, ProductId = productId, Slot = (short)slot, StorageKey = storageKey });
        else
            existing.StorageKey = storageKey;
    }

    public async Task ClearSlotAsync(Guid productId, int slot, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var existing = await databaseContext.ProductImages
            .SingleOrDefaultAsync(image => image.CompanyId == companyId && image.ProductId == productId && image.Slot == slot, cancellationToken);
        if (existing is not null) databaseContext.ProductImages.Remove(existing);
    }
}
