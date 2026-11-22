namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>
/// The result of a bulk price change. BatchId groups the price events. ConflictingVariantCount is how many sizes now have overlapping discounts
/// that the pricing engine could not decide (they are sold at the store price until an administrator resolves them). WasAlreadyApplied is true
/// when a retry with the same ClientTransactionId found the earlier result.
/// </summary>
public sealed record PriceChangeAppliedResponse(
    Guid BatchId,
    int ChangedVariantCount,
    int ConflictingVariantCount,
    bool WasAlreadyApplied);
