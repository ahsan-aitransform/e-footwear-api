using EFootwear.DAL.Persistence;
using EFootwear.Domain.Vendors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Vendors.Configurations;

/// <summary>Maps <see cref="Vendor"/> to contacts.vendors. change_seq (the sync cursor) is maintained by a trigger and is not mapped.</summary>
public sealed class VendorConfiguration : IEntityTypeConfiguration<Vendor>
{
    public void Configure(EntityTypeBuilder<Vendor> builder)
    {
        builder.ToTable("vendors", "contacts");
        builder.HasKey(vendor => new { vendor.CompanyId, vendor.Id });
        builder.Property(vendor => vendor.Id).ValueGeneratedOnAdd().HasDefaultValueSql("uuidv7()");
        builder.Property(vendor => vendor.Status).HasColumnName("status");
        builder.Property(vendor => vendor.CreatedAt).ValueGeneratedOnAdd().HasDefaultValueSql("now()");
        builder.ConfigureRowVersionConcurrencyToken();
    }
}
