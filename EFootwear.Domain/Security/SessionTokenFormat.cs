namespace EFootwear.Domain.Security;

/// <summary>
/// The shape of the access token the client sends as "Authorization: Bearer ...".
///   Company user:            {companyId}.{secret}
///   Platform administrator:  admin.{secret}
/// </summary>
public static class SessionTokenFormat
{
    public const char CompanySeparator = '.';
    public const string PlatformAdministratorPrefix = "admin.";
}
