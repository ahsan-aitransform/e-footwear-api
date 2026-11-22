namespace EFootwear.Domain.Security;

/// <summary>Values of sec.users.status (a CHECK constraint in the schema).</summary>
public static class UserStatusCodes
{
    public const string Active = "ACTIVE";
    public const string Locked = "LOCKED";
    public const string Disabled = "DISABLED";
}
