namespace EFootwear.Domain.Core.Settings;

/// <summary>
/// Technical size limits of settings values (column and layout limits, not business policy). Named here so the
/// backend rules and the frontend form limits (lib/constants) can be kept equal.
/// </summary>
public static class SettingsLimits
{
    public const int BusinessNameMaximumLength = 120;
    public const int BarcodeNameMaximumLength = 30;
    public const int PhoneNumberMaximumLength = 30;
    public const int EmailMaximumLength = 254;
    public const int AddressMaximumLength = 300;
    public const int InvoiceTextMaximumLength = 2000;
    public const int PaymentMethodDisplayNameMaximumLength = 60;
    public const int PaymentMethodSortOrderMaximum = 999;
    public const int LabelDimensionMinimumMillimetres = 10;
    public const int LabelDimensionMaximumMillimetres = 200;
    public const int ReceiptCopiesMinimum = 1;
    public const int ReceiptCopiesMaximum = 10;
    public const int BackdatedAdjustmentMaximumDaysLimit = 3650;
    public const int PercentageDecimalPlaces = 4;
}
