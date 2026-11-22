using EFootwear.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Security.Configurations;

/// <summary>Maps <see cref="RoleTemplatePermission"/> to sec.role_template_permissions.</summary>
public sealed class RoleTemplatePermissionConfiguration : IEntityTypeConfiguration<RoleTemplatePermission>
{
    public void Configure(EntityTypeBuilder<RoleTemplatePermission> builder)
    {
        builder.ToTable("role_template_permissions", "sec");
        builder.HasKey(permission => new { permission.CompanyId, permission.TemplateId, permission.PermissionCode });
    }
}
