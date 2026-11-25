using EFootwear.Domain.Vendors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Vendors.Configurations;

/// <summary>
/// Maps <see cref="VendorBalance"/> to contacts.vendor_balances for READING only. The table is a projection that a trigger keeps in step
/// with the journal; the application never inserts or updates it (Balance is a generated column).
/// </summary>
public sealed class VendorBalanceConfiguration : IEntityTypeConfiguration<VendorBalance>
{
    public void Configure(EntityTypeBuilder<VendorBalance> builder)
    {
        builder.ToTable("vendor_balances", "contacts", table => table.ExcludeFromMigrations());
        builder.HasKey(balance => new { balance.CompanyId, balance.VendorId });
        builder.Property(balance => balance.Balance).HasComputedColumnSql("debit_total - credit_total", stored: true);
    }
}
