using EFootwear.Domain.Catalogue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Catalogue.Configurations;

/// <summary>
/// Maps <see cref="VariantPrice"/> to catalog.variant_prices. change_seq (the sync cursor) is maintained by a trigger and row_ver is left alone
/// (nothing increments it), so neither is mapped.
/// </summary>
public sealed class VariantPriceConfiguration : IEntityTypeConfiguration<VariantPrice>
{
    public void Configure(EntityTypeBuilder<VariantPrice> builder)
    {
        builder.ToTable("variant_prices", "catalog");
        builder.HasKey(price => new { price.CompanyId, price.VariantId });
        builder.Property(price => price.UpdatedAt).ValueGeneratedOnAdd().HasDefaultValueSql("now()");
    }
}
