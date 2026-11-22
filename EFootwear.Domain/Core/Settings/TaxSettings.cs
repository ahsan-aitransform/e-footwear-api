namespace EFootwear.Domain.Core.Settings;

/// <summary>
/// Tax configuration "where applicable" (Part 7). Which tax rules apply is [BUSINESS DECISION REQUIRED]: these are only the
/// stored switches; nothing in the system applies a tax from them until the business defines the rules. Null = not configured.
/// </summary>
public sealed record TaxSettings(bool? IsTaxEnabled, decimal? DefaultTaxRatePercentage);
