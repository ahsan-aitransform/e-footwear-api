namespace EFootwear.BLL.Promotions.Contracts;

/// <summary>
/// Two or more promotions cover the same size in the same store: the engine does not pick one, it sells the size at the normal price until somebody chooses.
/// An open conflict is listed with the price each promotion would give.
/// </summary>
public sealed record PromotionConflictResponse(
    Guid GroupId,
    Guid StoreId,
    string StoreName,
    Guid VariantId,
    string Barcode,
    string ProductName,
    string SizeCode,
    long ListPrice,
    IReadOnlyList<PromotionConflictCandidateResponse> Candidates);
