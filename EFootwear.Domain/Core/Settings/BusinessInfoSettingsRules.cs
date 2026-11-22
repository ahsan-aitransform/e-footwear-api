using EFootwear.Domain.Common;

namespace EFootwear.Domain.Core.Settings;

public static class BusinessInfoSettingsRules
{
    /// <summary>Company level: the business name is required. Returns the trimmed values (empty text becomes null).</summary>
    public static BusinessInfoSettings NormalizeForCompany(BusinessInfoSettings settings)
    {
        var normalized = NormalizeCommon(settings) with
        {
            BusinessName = SettingsValueNormalizer.NormalizeOptionalText(
                settings.BusinessName, SettingsLimits.BusinessNameMaximumLength, "Business name"),
        };
        if (normalized.BusinessName is null)
            throw new DomainException(ErrorKind.Validation, "Business name is required.");
        return normalized;
    }

    /// <summary>Store level: overrides only; the business name cannot be overridden.</summary>
    public static BusinessInfoSettings NormalizeForStore(BusinessInfoSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BusinessName))
            throw new DomainException(ErrorKind.Validation,
                "A store cannot override the business name; change it in the company settings.");
        return NormalizeCommon(settings) with { BusinessName = null };
    }

    /// <summary>The values a store really uses: its own override where it has one, otherwise the company's value.</summary>
    public static BusinessInfoSettings Merge(BusinessInfoSettings company, BusinessInfoSettings storeOverrides) => new(
        company.BusinessName,
        storeOverrides.BarcodeName ?? company.BarcodeName,
        storeOverrides.BusinessNameUrdu ?? company.BusinessNameUrdu,
        storeOverrides.PhoneNumber ?? company.PhoneNumber,
        storeOverrides.MobileNumber ?? company.MobileNumber,
        storeOverrides.Email ?? company.Email,
        storeOverrides.AddressEnglish ?? company.AddressEnglish,
        storeOverrides.AddressUrdu ?? company.AddressUrdu,
        storeOverrides.InvoiceText ?? company.InvoiceText);

    private static BusinessInfoSettings NormalizeCommon(BusinessInfoSettings settings) => new(
        BusinessName: null,
        BarcodeName: SettingsValueNormalizer.NormalizeOptionalText(settings.BarcodeName, SettingsLimits.BarcodeNameMaximumLength, "Name for barcodes"),
        BusinessNameUrdu: SettingsValueNormalizer.NormalizeOptionalText(settings.BusinessNameUrdu, SettingsLimits.BusinessNameMaximumLength, "Business name (Urdu)"),
        PhoneNumber: SettingsValueNormalizer.NormalizeOptionalText(settings.PhoneNumber, SettingsLimits.PhoneNumberMaximumLength, "Phone number"),
        MobileNumber: SettingsValueNormalizer.NormalizeOptionalText(settings.MobileNumber, SettingsLimits.PhoneNumberMaximumLength, "Mobile number"),
        Email: SettingsValueNormalizer.NormalizeOptionalEmail(settings.Email, "Email"),
        AddressEnglish: SettingsValueNormalizer.NormalizeOptionalText(settings.AddressEnglish, SettingsLimits.AddressMaximumLength, "Address"),
        AddressUrdu: SettingsValueNormalizer.NormalizeOptionalText(settings.AddressUrdu, SettingsLimits.AddressMaximumLength, "Address (Urdu)"),
        InvoiceText: SettingsValueNormalizer.NormalizeOptionalText(settings.InvoiceText, SettingsLimits.InvoiceTextMaximumLength, "Invoice text"));
}
