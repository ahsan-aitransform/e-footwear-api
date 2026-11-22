namespace EFootwear.Domain.Core.Settings;

public static class UserPreferencesSettingsRules
{
    /// <summary>The default store is checked against the person's accessible stores by the use case, not here.</summary>
    public static UserPreferencesSettings Normalize(UserPreferencesSettings settings) => new(
        SettingsValueNormalizer.EnsureOptionalCode(settings.LanguageCode, SupportedLanguageCodes.All, "Language"),
        SettingsValueNormalizer.EnsureOptionalCode(settings.ThemeCode, ThemeCodes.All, "Theme"),
        settings.DefaultStoreId == Guid.Empty ? null : settings.DefaultStoreId);
}
