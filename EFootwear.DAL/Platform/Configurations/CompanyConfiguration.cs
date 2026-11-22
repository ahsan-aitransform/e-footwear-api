using EFootwear.DAL.Persistence;
using EFootwear.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Platform.Configurations;

/// <summary>Maps <see cref="Company"/> to platform.companies.</summary>
public sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("companies", "platform");
        builder.HasKey(company => company.Id);
        builder.Property(company => company.CurrencyCode).HasColumnType("character(3)");
        builder.Property(company => company.CreatedAt).ValueGeneratedOnAdd().HasDefaultValueSql("now()");
        builder.ConfigureRowVersionConcurrencyToken();
    }
}
