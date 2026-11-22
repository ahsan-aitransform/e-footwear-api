using EFootwear.DAL.Persistence;
using EFootwear.Domain.Catalogue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Catalogue.Configurations;

/// <summary>Maps <see cref="ProductVariant"/> to catalog.variants (one barcode per size and colour).</summary>
public sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("variants", "catalog");
        builder.HasKey(variant => new { variant.CompanyId, variant.Id });
        builder.Property(variant => variant.Id).ValueGeneratedOnAdd().HasDefaultValueSql("uuidv7()");
        builder.Property(variant => variant.CreatedAt).ValueGeneratedOnAdd().HasDefaultValueSql("now()");
        builder.ConfigureRowVersionConcurrencyToken();
    }
}
