using EFootwear.DAL.Persistence;
using EFootwear.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Security.Configurations;

/// <summary>Maps <see cref="PosDevice"/> to core.devices (row-level security limits it to the caller's stores).</summary>
public sealed class PosDeviceConfiguration : IEntityTypeConfiguration<PosDevice>
{
    public void Configure(EntityTypeBuilder<PosDevice> builder)
    {
        builder.ToTable("devices", "core");
        builder.HasKey(device => new { device.CompanyId, device.Id });
        builder.Property(device => device.Id).ValueGeneratedOnAdd().HasDefaultValueSql("uuidv7()");
        builder.Property(device => device.CreatedAt).ValueGeneratedOnAdd().HasDefaultValueSql("now()");
        builder.Ignore(device => device.Status);
        builder.ConfigureRowVersionConcurrencyToken();
    }
}
