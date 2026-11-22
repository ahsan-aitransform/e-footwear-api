namespace EFootwear.Domain.Core.Settings;

public static class PromotionPolicySettingsRules
{
    public static PromotionPolicySettings Normalize(PromotionPolicySettings settings) => new(
        SettingsValueNormalizer.EnsureOptionalPercentage(settings.MaximumManualDiscountPercentage, "Maximum manual discount"));
}
