using EFootwear.Domain.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Staff.Configurations;

/// <summary>
/// Maps <see cref="StaffBalance"/> to contacts.staff_balances for READING only. The table is a projection that a trigger keeps in step
/// with the journal; the application never inserts or updates it (Balance is a generated column).
/// </summary>
public sealed class StaffBalanceConfiguration : IEntityTypeConfiguration<StaffBalance>
{
    public void Configure(EntityTypeBuilder<StaffBalance> builder)
    {
        builder.ToTable("staff_balances", "contacts", table => table.ExcludeFromMigrations());
        builder.HasKey(balance => new { balance.CompanyId, balance.StaffId });
        builder.Property(balance => balance.Balance).HasComputedColumnSql("debit_total - credit_total", stored: true);
    }
}
