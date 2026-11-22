namespace EFootwear.Domain.Core.Settings;

/// <summary>
/// The names of the settings sections stored in core.company_settings / core.store_settings (column section_code).
/// A section is one JSON document; its shape is the record named in the comment.
/// </summary>
public static class SettingsSectionCodes
{
    /// <summary>Business Info tab: <see cref="BusinessInfoSettings"/>.</summary>
    public const string BusinessInfo = "BUSINESS_INFO";

    /// <summary>Date and time format, default language: <see cref="RegionalPreferencesSettings"/>.</summary>
    public const string RegionalPreferences = "REGIONAL_PREFERENCES";

    /// <summary>Print format, print language, labels, receipts: <see cref="PrintingSettings"/>.</summary>
    public const string Printing = "PRINTING";

    /// <summary>Tax switches: <see cref="TaxSettings"/>.</summary>
    public const string Tax = "TAX";

    /// <summary>Negative stock and backdated adjustments: <see cref="InventoryPolicySettings"/>.</summary>
    public const string InventoryPolicy = "INVENTORY_POLICY";

    /// <summary>Promotion limits: <see cref="PromotionPolicySettings"/>.</summary>
    public const string PromotionPolicy = "PROMOTION_POLICY";

    /// <summary>Which payment methods the company offers: <see cref="PaymentMethodsSettings"/>.</summary>
    public const string PaymentMethods = "PAYMENT_METHODS";
}
