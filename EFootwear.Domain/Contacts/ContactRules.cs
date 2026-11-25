using System.Text.RegularExpressions;
using EFootwear.Domain.Common;

namespace EFootwear.Domain.Contacts;

/// <summary>
/// Validation rules shared by customers, vendors and staff (name, phone, email, city, birth date). The database repeats the status
/// check. Rules that belong to one kind only live in CustomerRules, VendorRules and StaffRules.
/// </summary>
public static partial class ContactRules
{
    public const int NameMaximumLength = 120;
    public const int PhoneMaximumLength = 20;
    public const int EmailMaximumLength = 254;
    public const int DesignationMaximumLength = 80;

    [GeneratedRegex(@"^[+0-9][0-9 \-()]{4,19}$")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    public static bool IsValidPhone(string phone) => PhonePattern().IsMatch(phone);
    public static bool IsValidEmail(string email) => email.Length <= EmailMaximumLength && EmailPattern().IsMatch(email);

    public static void EnsureLength(string? text, int maximumLength, string what)
    {
        if (text is not null && text.Length > maximumLength)
            throw new DomainException(ErrorKind.Validation, $"{what} can have at most {maximumLength} characters.");
    }

    public static void EnsureNameIsGiven(string kindTitle, string name)
    {
        if (name.Length == 0) throw new DomainException(ErrorKind.Validation, $"The {kindTitle.ToLowerInvariant()} name is required.");
    }

    public static void EnsurePhoneIsGiven(string kindTitle, string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) throw new DomainException(ErrorKind.Validation, $"The {kindTitle.ToLowerInvariant()} phone number is required.");
    }

    public static void EnsureBirthDateIsNotInTheFuture(DateOnly? birthDate, DateOnly today)
    {
        if (birthDate is { } date && date > today)
            throw new DomainException(ErrorKind.Validation, "The birth date cannot be in the future.");
    }
}
