using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Common;

/// <summary>
/// Every barcode lookup also accepts a product code, and ignores capital letters: a person who types "5127s27" finds "5127S27". A barcode always
/// wins over a product code. Barcodes are unique with their capital letters, so two sizes may differ only in them; the barcode typed exactly wins,
/// and text that could mean more than one size means none of them (the screens then list the sizes of a product code to choose from).
/// </summary>
public static class BarcodeMatching
{
    /// <summary>
    /// The stored barcode that the typed barcode or product code means (a product code only when the product has exactly one size). When no single
    /// size matches, the text comes back unchanged and is then not found.
    /// </summary>
    public static async Task<string> FindStoredBarcodeAsync(
        ApplicationDatabaseContext databaseContext, Guid companyId, string typedBarcode, CancellationToken cancellationToken)
    {
        var variants = databaseContext.ProductVariants.AsNoTracking().Where(variant => variant.CompanyId == companyId && !variant.IsDeleted);
        // The usual scan is exact and uses the barcode index.
        if (await variants.AnyAsync(variant => variant.Barcode == typedBarcode, cancellationToken)) return typedBarcode;

        var lowerBarcode = typedBarcode.ToLowerInvariant();
        var matches = await variants.Where(variant => variant.Barcode.ToLower() == lowerBarcode)
            .Select(variant => variant.Barcode).Take(2).ToListAsync(cancellationToken);
        if (matches.Count > 0) return matches.Count == 1 ? matches[0] : typedBarcode;

        var sizesOfCode = await (from variant in variants
                                 join product in databaseContext.Products.AsNoTracking()
                                     on new { variant.CompanyId, Id = variant.ProductId } equals new { product.CompanyId, product.Id }
                                 where !product.IsDeleted && product.Code.ToLower() == lowerBarcode
                                 select variant.Barcode).Take(2).ToListAsync(cancellationToken);
        return sizesOfCode.Count == 1 ? sizesOfCode[0] : typedBarcode;
    }
}
