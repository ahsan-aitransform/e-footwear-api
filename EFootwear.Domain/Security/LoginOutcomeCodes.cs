namespace EFootwear.Domain.Security;

/// <summary>
/// The outcome strings returned by the sign-in SQL functions (sec.login and platform.admin_login).
/// "OK" means signed in, "LOCKED" means the account is locked, anything else is a refused sign-in.
/// </summary>
public static class LoginOutcomeCodes
{
    public const string Succeeded = "OK";
    public const string Invalid = "INVALID";
    public const string Locked = "LOCKED";
}
