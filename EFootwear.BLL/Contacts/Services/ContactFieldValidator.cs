using EFootwear.BLL.Abstractions;
using EFootwear.BLL.MasterData.Abstractions;
using EFootwear.Domain.Common;
using EFootwear.Domain.Contacts;
using EFootwear.Domain.MasterData;

namespace EFootwear.BLL.Contacts.Services;

/// <summary>
/// Checks and cleans the fields that customers, vendors and staff share: name, phone, email, birth date and city. The three services call
/// it so one rule is written once. <c>kindTitle</c> is Customer, Vendor or Staff and only shapes the error message.
/// </summary>
public sealed class ContactFieldValidator(IMasterListRepository masterListRepository, IDateTimeProvider dateTimeProvider)
{
    public string RequireName(string? name, string kindTitle)
    {
        var normalized = MasterListRules.NormalizeText(name);
        ContactRules.EnsureNameIsGiven(kindTitle, normalized);
        ContactRules.EnsureLength(normalized, ContactRules.NameMaximumLength, $"The {kindTitle.ToLowerInvariant()} name");
        return normalized;
    }

    public string RequirePhone(string? phone, string kindTitle)
    {
        var trimmed = MasterListRules.NormalizeOptionalText(phone);
        ContactRules.EnsurePhoneIsGiven(kindTitle, trimmed);
        if (trimmed!.Length > ContactRules.PhoneMaximumLength || !ContactRules.IsValidPhone(trimmed))
            throw new DomainException(ErrorKind.Validation, $"The {kindTitle.ToLowerInvariant()} phone number is not a valid phone number.");
        return trimmed;
    }

    public string? ValidateEmail(string? email)
    {
        var trimmed = MasterListRules.NormalizeOptionalText(email);
        if (trimmed is null) return null;
        if (!ContactRules.IsValidEmail(trimmed))
            throw new DomainException(ErrorKind.Validation, "The email address is not valid.");
        return trimmed;
    }

    public string? OptionalText(string? text, int maximumLength, string what)
    {
        var normalized = MasterListRules.NormalizeOptionalText(text);
        ContactRules.EnsureLength(normalized, maximumLength, what);
        return normalized;
    }

    public void EnsureBirthDateIsValid(DateOnly? birthDate) =>
        ContactRules.EnsureBirthDateIsNotInTheFuture(birthDate, DateOnly.FromDateTime(dateTimeProvider.UtcNow.UtcDateTime).AddDays(1));

    public async Task EnsureCityIsUsableAsync(Guid? cityId, CancellationToken cancellationToken)
    {
        if (cityId is null) return;
        var city = await masterListRepository.FindAsync(MasterListDefinitions.City, cityId.Value, cancellationToken);
        if (city is null || !city.IsActive)
            throw new DomainException(ErrorKind.Validation, "Choose an active city / area.");
    }

    public static void EnsureStatusIsValid(string? statusCode)
    {
        if (!ContactStatusCodes.IsValid(statusCode))
            throw new DomainException(ErrorKind.Validation, "The status must be ACTIVE or INACTIVE.");
    }
}
