using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Inventory.Abstractions;
using EFootwear.BLL.Inventory.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Inventory;

namespace EFootwear.BLL.Inventory.Services;

/// <summary>The lookups of the inventory entry screens: stores, an article size by barcode or by search (with the stock of the chosen store).</summary>
public sealed class InventoryLookupService(IInventoryReadRepository readRepository, ICurrentUser currentUser)
{
    public Task<IReadOnlyList<InventoryStoreResponse>> ListStoresAsync(CancellationToken cancellationToken) => readRepository.ListStoresAsync(cancellationToken);

    public async Task<InventoryItemResponse> FindItemByBarcodeAsync(Guid storeId, string? barcode, CancellationToken cancellationToken)
    {
        var cleanedBarcode = InventoryRules.CleanBarcode(barcode);
        EnsureStoreAccess(storeId);
        return await readRepository.FindItemByBarcodeAsync(storeId, cleanedBarcode, cancellationToken)
               ?? throw new DomainException(ErrorKind.NotFound, $"Barcode {cleanedBarcode} is not a known article size.");
    }

    public async Task<IReadOnlyList<InventoryItemResponse>> SearchItemsAsync(Guid storeId, string? searchText, CancellationToken cancellationToken)
    {
        EnsureStoreAccess(storeId);
        var cleanedText = searchText?.Trim();
        if (string.IsNullOrEmpty(cleanedText) || cleanedText.Length < 2) return [];
        return await readRepository.SearchItemsAsync(storeId, cleanedText, InventoryRules.SearchResultLimit, cancellationToken);
    }

    private void EnsureStoreAccess(Guid storeId)
    {
        if (storeId == Guid.Empty || !currentUser.CanAccessStore(storeId))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to this store.");
    }
}
