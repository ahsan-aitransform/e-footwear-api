using EFootwear.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Security.Configurations;

/// <summary>Maps <see cref="Role"/> to sec.roles.</summary>
public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles", "sec");
        builder.HasKey(role => role.Code);
    }
}
