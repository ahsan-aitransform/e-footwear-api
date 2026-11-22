using EFootwear.Domain.Common;

namespace EFootwear.Domain.Core.Settings;

public static class InventoryPolicySettingsRules
{
    public static InventoryPolicySettings Normalize(InventoryPolicySettings settings)
    {
        var maximumDays = SettingsValueNormalizer.EnsureOptionalIntegerInRange(settings.BackdatedStockAdjustmentMaximumDays,
            0, SettingsLimits.BackdatedAdjustmentMaximumDaysLimit, "Backdated adjustment limit (days)");
        if (maximumDays is not null && settings.AllowBackdatedStockAdjustments != true)
            throw new DomainException(ErrorKind.Validation,
                "A backdating limit in days only applies when backdated stock adjustments are allowed.");
        return new InventoryPolicySettings(settings.AllowNegativeStock, settings.AllowBackdatedStockAdjustments, maximumDays);
    }
}
