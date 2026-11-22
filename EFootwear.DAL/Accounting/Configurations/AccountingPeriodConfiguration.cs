using EFootwear.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Accounting.Configurations;

/// <summary>Maps <see cref="AccountingPeriod"/> to acct.periods (baseline schema). The table has no row_ver column: closing and reopening are rare, single-person actions.</summary>
public sealed class AccountingPeriodConfiguration : IEntityTypeConfiguration<AccountingPeriod>
{
    public void Configure(EntityTypeBuilder<AccountingPeriod> builder)
    {
        builder.ToTable("periods", "acct");
        builder.HasKey(period => new { period.CompanyId, period.PeriodStart });
    }
}
