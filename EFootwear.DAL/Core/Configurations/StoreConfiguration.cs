using EFootwear.DAL.Persistence;
using EFootwear.Domain.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Core.Configurations;

/// <summary>Maps <see cref="Store"/> to core.stores.</summary>
public sealed class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> builder)
    {
        builder.ToTable("stores", "core");
        builder.HasKey(store => new { store.CompanyId, store.Id });
        builder.Property(store => store.Id).ValueGeneratedOnAdd().HasDefaultValueSql("uuidv7()");
        builder.ConfigureRowVersionConcurrencyToken();
    }
}
