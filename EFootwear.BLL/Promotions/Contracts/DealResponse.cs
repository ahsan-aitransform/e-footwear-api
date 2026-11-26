namespace EFootwear.BLL.Promotions.Contracts;

/// <summary>A deal: one barcode that sells several products, each at its net price (promo.bundles). The list endpoint leaves <see cref="Lines"/> empty and fills the counts.</summary>
public sealed record DealResponse(
    Guid Id,
    string Name,
    string Barcode,
    long BundlePrice,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    bool IsActive,
    string PhaseCode,
    int LineCount,
    decimal TotalQuantity,
    IReadOnlyList<DealLineResponse> Lines,
    long RowVersion);
