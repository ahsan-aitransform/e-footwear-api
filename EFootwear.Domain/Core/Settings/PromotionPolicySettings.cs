namespace EFootwear.Domain.Core.Settings;

/// <summary>
/// Promotion settings (Part 7, Part 18 "maximum discount rules"). Null = not configured. Stacking and priority are
/// [BUSINESS DECISION REQUIRED] and are not settings here.
/// </summary>
public sealed record PromotionPolicySettings(decimal? MaximumManualDiscountPercentage);
