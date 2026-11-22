using EFootwear.Domain.Catalogue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Catalogue.Configurations;

/// <summary>Maps <see cref="ProductImage"/> to catalog.product_images (unique per product and slot).</summary>
public sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("product_images", "catalog");
        builder.HasKey(image => new { image.CompanyId, image.Id });
        builder.Property(image => image.Id).ValueGeneratedOnAdd().HasDefaultValueSql("uuidv7()");
    }
}
