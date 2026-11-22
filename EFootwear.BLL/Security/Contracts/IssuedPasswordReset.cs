namespace EFootwear.BLL.Security.Contracts;

/// <summary>What sec.request_password_reset returned: the user, the secret part of the token and the phone number on record.</summary>
public sealed record IssuedPasswordReset(Guid UserId, string TokenSecret, string? DeliveryPhoneNumber);
