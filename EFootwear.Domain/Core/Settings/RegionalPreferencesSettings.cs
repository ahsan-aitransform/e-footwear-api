namespace EFootwear.Domain.Core.Settings;

/// <summary>Date and time display and the default language. Time zone and currency are in the company profile. Null = not configured.</summary>
public sealed record RegionalPreferencesSettings(string? DateFormat, string? TimeFormat, string? DefaultLanguageCode);
