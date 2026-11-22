using EFootwear.Domain.Common;

namespace EFootwear.Domain.Security;

/// <summary>
/// A company's named set of permission codes (table sec.role_templates, codes in sec.role_template_permissions).
/// It only PREFILLS a user's permissions; the user's own rows in sec.user_permissions stay the truth, so a template never
/// blocks an individual override (Part 6). The built-in suggestions per system role are in DefaultRolePermissionTemplates.
/// </summary>
public sealed class RoleTemplate : IHasRowVersion
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public long RowVersion { get; set; }
}
