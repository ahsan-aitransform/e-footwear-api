using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Common;

/// <summary>
/// Barcode lookups ignore capital letters: a person who types "5127s27" finds "5127S27". Barcodes are unique with their capital letters, so two
/// sizes may differ only in them; the barcode typed exactly always wins, and text that could mean more than one size means none of them.
/// </summary>
public static class BarcodeMatching
{
    /// <summary>The stored barcode that the typed text means. When no single size matches, the text comes back unchanged and is then not found.</summary>
    public static async Task<string> FindStoredBarcodeAsync(
        ApplicationDatabaseContext databaseContext, Guid companyId, string typedBarcode, CancellationToken cancellationToken)
    {
        var variants = databaseContext.ProductVariants.AsNoTracking().Where(variant => variant.CompanyId == companyId && !variant.IsDeleted);
        // The usual scan is exact and uses the barcode index.
        if (await variants.AnyAsync(variant => variant.Barcode == typedBarcode, cancellationToken)) return typedBarcode;

        var lowerBarcode = typedBarcode.ToLowerInvariant();
        var matches = await variants.Where(variant => variant.Barcode.ToLower() == lowerBarcode)
            .Select(variant => variant.Barcode).Take(2).ToListAsync(cancellationToken);
        return matches.Count == 1 ? matches[0] : typedBarcode;
    }
}
