namespace EFootwear.Domain.Security;

/// <summary>
/// One permission given to one user. Table: sec.user_permissions. Rows are copied from a role template when the user is
/// created and can then be changed one by one (the template never prevents an individual override).
/// </summary>
public sealed class UserPermissionGrant
{
    public Guid CompanyId { get; set; }
    public Guid UserId { get; set; }
    public string PermissionCode { get; set; } = "";
    public Guid? GrantedBy { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
}
