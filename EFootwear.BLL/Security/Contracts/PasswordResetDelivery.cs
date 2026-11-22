namespace EFootwear.BLL.Security.Contracts;

/// <summary>Everything a delivery channel needs to get a password reset token to a user. The token is a secret: never log it.</summary>
public sealed record PasswordResetDelivery(Guid CompanyId, Guid UserId, string Email, string? PhoneNumber,
                                           string ResetToken, DateTimeOffset ExpiresAt);
