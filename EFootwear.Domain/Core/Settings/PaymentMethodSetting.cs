namespace EFootwear.Domain.Core.Settings;

/// <summary>How a company offers one payment method: switched on or off, with its own display names and list position.</summary>
public sealed record PaymentMethodSetting(
    string Code,
    bool IsEnabled,
    string? DisplayNameEnglish,
    string? DisplayNameUrdu,
    int SortOrder);
