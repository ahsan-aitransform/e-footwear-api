using EFootwear.BLL.Promotions.Contracts;

namespace EFootwear.BLL.Promotions.Abstractions;

/// <summary>Deals (promo.bundles and promo.bundle_items): one barcode that sells several products at their net prices. Implemented in the DAL.</summary>
public interface IPromotionDealRepository
{
    /// <summary>The not deleted deals, newest first, without lines (the counts are filled).</summary>
    Task<IReadOnlyList<DealResponse>> ListAsync(CancellationToken cancellationToken);

    Task<DealResponse?> FindAsync(Guid dealId, CancellationToken cancellationToken);

    /// <summary>The size a scanned barcode belongs to, with its current sale price, or null.</summary>
    Task<DealVariantLookupResponse?> FindVariantByBarcodeAsync(string barcode, CancellationToken cancellationToken);

    /// <summary>How many of the given sizes exist (not deleted) in the company.</summary>
    Task<int> CountExistingVariantsAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken);

    /// <summary>The next free deal barcode, built by the system.</summary>
    Task<string> BuildFreeBarcodeAsync(CancellationToken cancellationToken);

    /// <summary>Creates the deal and its lines. Returns the new id, or null when the barcode is already used by a size or another deal.</summary>
    Task<Guid?> CreateAsync(SaveDealRequest request, string barcode, long bundlePrice, bool isActive, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the deal and its lines when the row version is still the one given. False means no such deal with that version. A barcode already used by a size or another
    /// deal is refused with a conflict.
    /// </summary>
    Task<bool> UpdateAsync(Guid dealId, SaveDealRequest request, string barcode, long bundlePrice, long expectedRowVersion, Guid userId, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid dealId, Guid userId, CancellationToken cancellationToken);
}
