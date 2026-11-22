using EFootwear.DAL.Persistence;
using EFootwear.Domain.MasterData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.MasterData.Configurations;

/// <summary>Maps <see cref="Category"/> to master.categories.</summary>
public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories", "master");
        builder.HasKey(category => new { category.CompanyId, category.Id });
        builder.Property(category => category.Id).ValueGeneratedOnAdd().HasDefaultValueSql("uuidv7()");
        builder.Property(category => category.ParentCategoryId).HasColumnName("parent_id");
        builder.Property(category => category.NameUrdu).HasColumnName("name_ur");
        builder.ConfigureRowVersionConcurrencyToken();
    }
}
