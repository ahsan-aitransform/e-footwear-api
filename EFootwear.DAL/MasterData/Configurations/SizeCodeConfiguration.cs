using EFootwear.Domain.MasterData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.MasterData.Configurations;

/// <summary>Maps <see cref="SizeCode"/> to master.sizes (the size column of the table is called size_code).</summary>
public sealed class SizeCodeConfiguration : IEntityTypeConfiguration<SizeCode>
{
    public void Configure(EntityTypeBuilder<SizeCode> builder)
    {
        builder.ToTable("sizes", "master");
        builder.HasKey(size => new { size.CompanyId, size.Id });
        builder.Property(size => size.Id).ValueGeneratedOnAdd().HasDefaultValueSql("uuidv7()");
        builder.Property(size => size.Code).HasColumnName("size_code");
    }
}
