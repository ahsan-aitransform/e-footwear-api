using EFootwear.Domain.Common;

namespace EFootwear.Domain.Platform;

/// <summary>The validation rules for a company.</summary>
public static class CompanyRules
{
    public const int NameMaximumLength = 120;

    public static void EnsureValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMaximumLength)
            throw new DomainException(ErrorKind.Validation,
                $"Company name is required (up to {NameMaximumLength} characters).");
    }
}
