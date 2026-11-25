using EFootwear.DAL.Persistence;
using EFootwear.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Customers.Configurations;

/// <summary>Maps <see cref="Customer"/> to contacts.customers. change_seq (the sync cursor) is maintained by a trigger and is not mapped.</summary>
public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers", "contacts");
        builder.HasKey(customer => new { customer.CompanyId, customer.Id });
        builder.Property(customer => customer.Id).ValueGeneratedOnAdd().HasDefaultValueSql("uuidv7()");
        builder.Property(customer => customer.Status).HasColumnName("status");
        builder.Property(customer => customer.CreatedAt).ValueGeneratedOnAdd().HasDefaultValueSql("now()");
        builder.ConfigureRowVersionConcurrencyToken();
    }
}
