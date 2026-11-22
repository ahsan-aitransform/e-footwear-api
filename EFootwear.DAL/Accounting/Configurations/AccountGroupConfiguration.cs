using EFootwear.DAL.Persistence;
using EFootwear.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Accounting.Configurations;

/// <summary>Maps <see cref="AccountGroup"/> to acct.account_groups (migration 0200).</summary>
public sealed class AccountGroupConfiguration : IEntityTypeConfiguration<AccountGroup>
{
    public void Configure(EntityTypeBuilder<AccountGroup> builder)
    {
        builder.ToTable("account_groups", "acct");
        builder.HasKey(group => new { group.CompanyId, group.GroupCode });
        builder.Property(group => group.MainTypeCode).HasColumnName("main_type");
        builder.ConfigureRowVersionConcurrencyToken();
    }
}
