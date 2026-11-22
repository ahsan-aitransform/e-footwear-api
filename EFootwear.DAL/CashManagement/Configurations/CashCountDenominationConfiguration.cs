using EFootwear.Domain.CashManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.CashManagement.Configurations;

/// <summary>Maps <see cref="CashCountDenomination"/> to cash.count_denominations (one row per denomination of a count).</summary>
public sealed class CashCountDenominationConfiguration : IEntityTypeConfiguration<CashCountDenomination>
{
    public void Configure(EntityTypeBuilder<CashCountDenomination> builder)
    {
        builder.ToTable("count_denominations", "cash");
        builder.HasKey(line => new { line.CompanyId, line.CountId, line.Denomination });
    }
}
