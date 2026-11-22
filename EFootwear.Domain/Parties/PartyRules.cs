using System.Text.RegularExpressions;
using EFootwear.Domain.Common;

namespace EFootwear.Domain.Parties;

/// <summary>Validation rules for parties (docx 3.1 and Part 8). The database repeats the CNIC shape and the role check.</summary>
public static partial class PartyRules
{
    public const int NameMaximumLength = 120;
    public const int AddressMaximumLength = 500;
    public const int NotesMaximumLength = 1000;
    public const int PhoneMaximumLength = 20;
    public const int DesignationMaximumLength = 80;
    public const int EmailMaximumLength = 254;

    [GeneratedRegex("^[0-9]{5}-[0-9]{7}-[0-9]$")]
    private static partial Regex CnicPattern();

    [GeneratedRegex(@"^[+0-9][0-9 \-()]{4,19}$")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    public static bool IsValidCnic(string cnic) => CnicPattern().IsMatch(cnic);
    public static bool IsValidPhone(string phone) => PhonePattern().IsMatch(phone);
    public static bool IsValidEmail(string email) => email.Length <= EmailMaximumLength && EmailPattern().IsMatch(email);

    public static void EnsureAtLeastOneRole(bool isCustomer, bool isVendor, bool isStaff)
    {
        if (!isCustomer && !isVendor && !isStaff)
            throw new DomainException(ErrorKind.Validation, "Choose at least one party type: customer, vendor or staff.");
    }

    public static void EnsureLength(string? text, int maximumLength, string what)
    {
        if (text is not null && text.Length > maximumLength)
            throw new DomainException(ErrorKind.Validation, $"{what} can have at most {maximumLength} characters.");
    }
}
