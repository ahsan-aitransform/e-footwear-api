using EFootwear.Domain.Common;

namespace EFootwear.Domain.Core.Settings;

public static class TaxSettingsRules
{
    public static TaxSettings Normalize(TaxSettings settings)
    {
        var rate = SettingsValueNormalizer.EnsureOptionalPercentage(settings.DefaultTaxRatePercentage, "Default tax rate");
        if (rate is not null && settings.IsTaxEnabled is null)
            throw new DomainException(ErrorKind.Validation, "Choose whether tax is enabled before setting a tax rate.");
        return new TaxSettings(settings.IsTaxEnabled, rate);
    }
}
