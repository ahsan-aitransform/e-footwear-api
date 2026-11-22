using EFootwear.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Security.Configurations;

/// <summary>Maps <see cref="UserPermissionGrant"/> to sec.user_permissions.</summary>
public sealed class UserPermissionGrantConfiguration : IEntityTypeConfiguration<UserPermissionGrant>
{
    public void Configure(EntityTypeBuilder<UserPermissionGrant> builder)
    {
        builder.ToTable("user_permissions", "sec");
        builder.HasKey(grant => new { grant.CompanyId, grant.UserId, grant.PermissionCode });
        builder.Property(grant => grant.GrantedAt).ValueGeneratedOnAdd().HasDefaultValueSql("now()");
    }
}
