namespace EFootwear.Domain.Promotions;

/// <summary>
/// The stored status of a rule or a deal (promo.rules.status, promo.bundles.status). "Active" on the screens is PUBLISHED and "In Active" is DRAFT; a deleted promotion
/// is CANCELLED and marked deleted, never removed, so an invoice that used it can still name it.
/// </summary>
public static class PromotionStatusCodes
{
    public const string Draft = "DRAFT";
    public const string Published = "PUBLISHED";
    public const string Cancelled = "CANCELLED";
}
