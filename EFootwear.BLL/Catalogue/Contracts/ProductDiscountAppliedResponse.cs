namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>
/// The result of a product discount operation. ConflictingVariantCount: sizes that now have overlapping discounts the engine cannot decide.
/// RestoredPriceVariantCount and SkippedRestoreVariantCount: sizes whose previous sale price was put back, and sizes where no clear previous
/// price exists (their price is left as it is).
/// </summary>
public sealed record ProductDiscountAppliedResponse(
    Guid BatchId,
    int AffectedProductCount,
    int AffectedStoreCount,
    int ConflictingVariantCount,
    int RestoredPriceVariantCount,
    int SkippedRestoreVariantCount,
    bool WasAlreadyApplied);
