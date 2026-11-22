using EFootwear.Domain.Common;

namespace EFootwear.Domain.Security;

/// <summary>
/// The rules for a new password (docx 1.5 Change Password and 1.1 Create New User): at least 8 characters, letters and digits,
/// and different from the current one. One place, used by create user, change password, administrator reset and the reset by token.
/// Passwords are never stored in plain text: the data layer only ever sees the Argon2id hash.
/// </summary>
public static class PasswordRules
{
    public const int MinimumLength = 8;
    public const int MaximumLength = 200;

    /// <param name="newPassword">The password the person typed.</param>
    /// <param name="currentPassword">When given, the new password must differ from it.</param>
    /// <param name="subjectName">How the message calls the password ("New password", "Temporary password").</param>
    public static void EnsureValid(string? newPassword, string? currentPassword = null, string subjectName = "New password")
    {
        var password = newPassword ?? "";
        if (password.Length < MinimumLength)
            throw new DomainException(ErrorKind.Validation, $"{subjectName} needs at least {MinimumLength} characters.");
        if (password.Length > MaximumLength)
            throw new DomainException(ErrorKind.Validation, $"{subjectName} can have at most {MaximumLength} characters.");
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new DomainException(ErrorKind.Validation, $"{subjectName} needs letters and digits.");
        if (currentPassword is not null && password == currentPassword)
            throw new DomainException(ErrorKind.Validation, $"{subjectName} must be different from the current one.");
    }
}
