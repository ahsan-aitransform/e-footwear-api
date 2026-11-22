using EFootwear.Domain.Catalogue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Catalogue.Configurations;

/// <summary>Maps <see cref="StorePriceOverride"/> to catalog.store_price_overrides.</summary>
public sealed class StorePriceOverrideConfiguration : IEntityTypeConfiguration<StorePriceOverride>
{
    public void Configure(EntityTypeBuilder<StorePriceOverride> builder)
    {
        builder.ToTable("store_price_overrides", "catalog");
        builder.HasKey(priceOverride => new { priceOverride.CompanyId, priceOverride.StoreId, priceOverride.VariantId });
        builder.Property(priceOverride => priceOverride.UpdatedAt).ValueGeneratedOnAdd().HasDefaultValueSql("now()");
    }
}
