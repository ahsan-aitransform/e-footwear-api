namespace EFootwear.Domain.Security;

/// <summary>One permission code inside a role template. Table: sec.role_template_permissions.</summary>
public sealed class RoleTemplatePermission
{
    public Guid CompanyId { get; set; }
    public Guid TemplateId { get; set; }
    public string PermissionCode { get; set; } = "";
}
