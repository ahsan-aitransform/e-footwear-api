using EFootwear.DAL.Persistence;
using EFootwear.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Accounting.Configurations;

/// <summary>Maps <see cref="GeneralLedgerAccount"/> to acct.accounts.</summary>
public sealed class GeneralLedgerAccountConfiguration : IEntityTypeConfiguration<GeneralLedgerAccount>
{
    public void Configure(EntityTypeBuilder<GeneralLedgerAccount> builder)
    {
        builder.ToTable("accounts", "acct");
        builder.HasKey(account => new { account.CompanyId, account.Id });
        builder.Property(account => account.Id).ValueGeneratedOnAdd().HasDefaultValueSql("uuidv7()");
        builder.Property(account => account.MainTypeCode).HasColumnName("main_type");
        builder.Property(account => account.ParentAccountId).HasColumnName("parent_id");
        builder.ConfigureRowVersionConcurrencyToken();
    }
}
