namespace EFootwear.Domain.Core.Settings;

public static class PrintingSettingsRules
{
    /// <summary>The values a store really uses: its own override where it has one, otherwise the company's value.</summary>
    public static PrintingSettings Merge(PrintingSettings company, PrintingSettings storeOverrides) => new(
        storeOverrides.DefaultPrintFormat ?? company.DefaultPrintFormat,
        storeOverrides.PrintLanguageCode ?? company.PrintLanguageCode,
        storeOverrides.PrintRightToLeft ?? company.PrintRightToLeft,
        storeOverrides.PrintLogoOnDocuments ?? company.PrintLogoOnDocuments,
        storeOverrides.BarcodeLabelWidthMillimetres ?? company.BarcodeLabelWidthMillimetres,
        storeOverrides.BarcodeLabelHeightMillimetres ?? company.BarcodeLabelHeightMillimetres,
        storeOverrides.ReceiptCopies ?? company.ReceiptCopies);

    public static PrintingSettings Normalize(PrintingSettings settings) => new(
        SettingsValueNormalizer.EnsureOptionalCode(settings.DefaultPrintFormat, PrintFormatCodes.All, "Default print format"),
        SettingsValueNormalizer.EnsureOptionalCode(settings.PrintLanguageCode, SupportedLanguageCodes.All, "Print language"),
        settings.PrintRightToLeft,
        settings.PrintLogoOnDocuments,
        SettingsValueNormalizer.EnsureOptionalIntegerInRange(settings.BarcodeLabelWidthMillimetres,
            SettingsLimits.LabelDimensionMinimumMillimetres, SettingsLimits.LabelDimensionMaximumMillimetres, "Barcode label width"),
        SettingsValueNormalizer.EnsureOptionalIntegerInRange(settings.BarcodeLabelHeightMillimetres,
            SettingsLimits.LabelDimensionMinimumMillimetres, SettingsLimits.LabelDimensionMaximumMillimetres, "Barcode label height"),
        SettingsValueNormalizer.EnsureOptionalIntegerInRange(settings.ReceiptCopies,
            SettingsLimits.ReceiptCopiesMinimum, SettingsLimits.ReceiptCopiesMaximum, "Receipt copies"));
}
