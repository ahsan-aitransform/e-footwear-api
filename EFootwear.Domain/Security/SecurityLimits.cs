using EFootwear.Domain.Common;

namespace EFootwear.Domain.Security;

/// <summary>Length limits and checks for the text fields of the security screens (role templates, devices, revoke reasons).</summary>
public static class SecurityLimits
{
    public const int MaximumRoleTemplateNameLength = 80;
    public const int MaximumDescriptionLength = 400;
    public const int MaximumDeviceLabelLength = 80;
    public const int MaximumReasonLength = 200;

    public static string EnsureRequiredText(string? text, string fieldName, int maximumLength)
    {
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0)
            throw new DomainException(ErrorKind.Validation, $"{fieldName} is required.");
        if (trimmed.Length > maximumLength)
            throw new DomainException(ErrorKind.Validation, $"{fieldName} can have at most {maximumLength} characters.");
        return trimmed;
    }

    public static string? EnsureOptionalText(string? text, string fieldName, int maximumLength)
    {
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > maximumLength)
            throw new DomainException(ErrorKind.Validation, $"{fieldName} can have at most {maximumLength} characters.");
        return trimmed;
    }
}
