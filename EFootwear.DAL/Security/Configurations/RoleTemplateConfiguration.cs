using EFootwear.DAL.Persistence;
using EFootwear.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Security.Configurations;

/// <summary>Maps <see cref="RoleTemplate"/> to sec.role_templates.</summary>
public sealed class RoleTemplateConfiguration : IEntityTypeConfiguration<RoleTemplate>
{
    public void Configure(EntityTypeBuilder<RoleTemplate> builder)
    {
        builder.ToTable("role_templates", "sec");
        builder.HasKey(template => new { template.CompanyId, template.Id });
        builder.Property(template => template.Id).ValueGeneratedOnAdd().HasDefaultValueSql("uuidv7()");
        builder.Property(template => template.CreatedAt).ValueGeneratedOnAdd().HasDefaultValueSql("now()");
        builder.ConfigureRowVersionConcurrencyToken();

        // The relationship (no navigation property needed) makes EF insert a template before its permission rows,
        // matching the foreign key and ON DELETE CASCADE of sec.role_template_permissions.
        builder.HasMany<RoleTemplatePermission>()
            .WithOne()
            .HasForeignKey(permission => new { permission.CompanyId, permission.TemplateId })
            .HasPrincipalKey(template => new { template.CompanyId, template.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
