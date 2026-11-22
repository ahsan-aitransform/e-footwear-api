using EFootwear.DAL.Persistence;
using EFootwear.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Security.Configurations;

/// <summary>Maps <see cref="CompanyUser"/> to sec.users.</summary>
public sealed class CompanyUserConfiguration : IEntityTypeConfiguration<CompanyUser>
{
    public void Configure(EntityTypeBuilder<CompanyUser> builder)
    {
        builder.ToTable("users", "sec");
        builder.HasKey(user => new { user.CompanyId, user.Id });
        builder.Property(user => user.Id).ValueGeneratedOnAdd().HasDefaultValueSql("uuidv7()");
        builder.Property(user => user.CreatedAt).ValueGeneratedOnAdd().HasDefaultValueSql("now()");
        builder.ConfigureRowVersionConcurrencyToken();
    }
}
