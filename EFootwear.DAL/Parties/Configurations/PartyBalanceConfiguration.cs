using EFootwear.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Parties.Configurations;

/// <summary>
/// Maps <see cref="PartyBalance"/> to party.party_balances for READING only. The table is a projection that a trigger keeps in step
/// with the journal; the application never inserts or updates it (Balance is a generated column).
/// </summary>
public sealed class PartyBalanceConfiguration : IEntityTypeConfiguration<PartyBalance>
{
    public void Configure(EntityTypeBuilder<PartyBalance> builder)
    {
        builder.ToTable("party_balances", "party", table => table.ExcludeFromMigrations());
        builder.HasKey(balance => new { balance.CompanyId, balance.PartyId });
        builder.Property(balance => balance.Balance).HasComputedColumnSql("debit_total - credit_total", stored: true);
    }
}
