namespace EFootwear.BLL.Promotions.Contracts;

/// <summary>
/// Creates or replaces a deal. An empty <see cref="Barcode"/> makes the system give the deal its own barcode (docx 2.12 improvement). The price of the whole deal is worked out
/// from the lines. <see cref="RowVersion"/> is required when changing a deal.
/// </summary>
public sealed record SaveDealRequest(
    string Name,
    string? Barcode,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    bool IsActive,
    IReadOnlyList<SaveDealLineRequest> Lines,
    long? RowVersion);
