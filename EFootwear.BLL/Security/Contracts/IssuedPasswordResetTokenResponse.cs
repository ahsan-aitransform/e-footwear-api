namespace EFootwear.BLL.Security.Contracts;

/// <summary>
/// A single-use password reset token issued by an administrator. It is shown once; the administrator hands it to the user.
/// How tokens reach users by themselves (email, SMS) is [BUSINESS DECISION REQUIRED]: no delivery channel is configured.
/// </summary>
public sealed record IssuedPasswordResetTokenResponse(string ResetToken, DateTimeOffset ExpiresAt);
