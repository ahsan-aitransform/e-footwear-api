using EFootwear.DAL.Persistence;
using EFootwear.Domain.Catalogue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Catalogue.Configurations;

/// <summary>Maps <see cref="PriceBand"/> to catalog.price_bands.</summary>
public sealed class PriceBandConfiguration : IEntityTypeConfiguration<PriceBand>
{
    public void Configure(EntityTypeBuilder<PriceBand> builder)
    {
        builder.ToTable("price_bands", "catalog");
        builder.HasKey(priceBand => new { priceBand.CompanyId, priceBand.Id });
        builder.Property(priceBand => priceBand.Id).ValueGeneratedOnAdd().HasDefaultValueSql("uuidv7()");
        builder.Property(priceBand => priceBand.CreatedAt).ValueGeneratedOnAdd().HasDefaultValueSql("now()");
        builder.ConfigureRowVersionConcurrencyToken();
    }
}
