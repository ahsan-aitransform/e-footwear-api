using EFootwear.DAL.Persistence;
using EFootwear.Domain.Catalogue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Catalogue.Configurations;

/// <summary>Maps <see cref="Product"/> to catalog.products. The extra attributes are a jsonb column; the application only ever writes a JSON object.</summary>
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products", "catalog");
        builder.HasKey(product => new { product.CompanyId, product.Id });
        builder.Property(product => product.Id).ValueGeneratedOnAdd().HasDefaultValueSql("uuidv7()");
        builder.Property(product => product.NameUrdu).HasColumnName("name_ur");
        builder.Property(product => product.Weight).HasColumnType("numeric(10,3)");
        builder.Property(product => product.TaxRate).HasColumnType("numeric(7,4)");
        builder.Property(product => product.AttributesJson).HasColumnName("attributes").HasColumnType("jsonb");
        builder.Property(product => product.CreatedAt).ValueGeneratedOnAdd().HasDefaultValueSql("now()");
        builder.ConfigureRowVersionConcurrencyToken();
    }
}
