namespace EFootwear.Domain.Core.Settings;

public static class RegionalPreferencesSettingsRules
{
    public static RegionalPreferencesSettings Normalize(RegionalPreferencesSettings settings) => new(
        SettingsValueNormalizer.EnsureOptionalCode(settings.DateFormat, DateFormatCodes.All, "Date format"),
        SettingsValueNormalizer.EnsureOptionalCode(settings.TimeFormat, TimeFormatCodes.All, "Time format"),
        SettingsValueNormalizer.EnsureOptionalCode(settings.DefaultLanguageCode, SupportedLanguageCodes.All, "Default language"));
}
