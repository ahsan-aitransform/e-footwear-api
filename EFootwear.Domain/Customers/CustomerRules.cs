using System.Text.RegularExpressions;

namespace EFootwear.Domain.Customers;

/// <summary>Facts about customers: the walk-in customer is created by the database for every company and has no phone number.</summary>
public static partial class CustomerRules
{
    public const string WalkInCustomerCode = "WALKIN";

    // 13 digits, typed either without dashes or as 12345-1234567-1.
    [GeneratedRegex(@"^([0-9]{13}|[0-9]{5}-[0-9]{7}-[0-9])$")]
    private static partial Regex CnicPattern();

    public static bool IsValidCnic(string cnic) => CnicPattern().IsMatch(cnic);

    /// <summary>The one stored spelling of a valid CNIC: 12345-1234567-1.</summary>
    public static string FormatCnic(string validCnic)
    {
        var digits = validCnic.Replace("-", "");
        return $"{digits[..5]}-{digits[5..12]}-{digits[12..]}";
    }
}
