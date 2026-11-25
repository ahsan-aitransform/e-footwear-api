using EFootwear.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Customers.Configurations;

/// <summary>
/// Maps <see cref="CustomerBalance"/> to contacts.customer_balances for READING only. The table is a projection that a trigger keeps in step
/// with the journal; the application never inserts or updates it (Balance is a generated column).
/// </summary>
public sealed class CustomerBalanceConfiguration : IEntityTypeConfiguration<CustomerBalance>
{
    public void Configure(EntityTypeBuilder<CustomerBalance> builder)
    {
        builder.ToTable("customer_balances", "contacts", table => table.ExcludeFromMigrations());
        builder.HasKey(balance => new { balance.CompanyId, balance.CustomerId });
        builder.Property(balance => balance.Balance).HasComputedColumnSql("debit_total - credit_total", stored: true);
    }
}
