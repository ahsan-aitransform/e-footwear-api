namespace EFootwear.Domain.Core.Settings;

/// <summary>A person's own preferences, kept on the server so they follow the person to another device. Null = no choice made.</summary>
public sealed record UserPreferencesSettings(string? LanguageCode, string? ThemeCode, Guid? DefaultStoreId);
