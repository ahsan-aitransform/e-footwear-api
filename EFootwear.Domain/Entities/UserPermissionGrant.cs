namespace EFootwear.Domain.Entities;

/// <summary>An individual permission given to a user on top of their role. Table: sec.user_permissions.</summary>
public sealed class UserPermissionGrant
{
    public Guid CompanyId { get; set; }
    public Guid UserId { get; set; }
    public string PermissionCode { get; set; } = "";
    public Guid? GrantedBy { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
}
