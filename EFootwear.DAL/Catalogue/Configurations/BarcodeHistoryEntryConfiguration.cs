using EFootwear.Domain.Catalogue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Catalogue.Configurations;

/// <summary>Maps <see cref="BarcodeHistoryEntry"/> to catalog.barcode_history (append-only, migration 0300).</summary>
public sealed class BarcodeHistoryEntryConfiguration : IEntityTypeConfiguration<BarcodeHistoryEntry>
{
    public void Configure(EntityTypeBuilder<BarcodeHistoryEntry> builder)
    {
        builder.ToTable("barcode_history", "catalog");
        builder.HasKey(entry => new { entry.CompanyId, entry.Id });
        builder.Property(entry => entry.Id).ValueGeneratedOnAdd().HasDefaultValueSql("uuidv7()");
        builder.Property(entry => entry.ChangedAt).ValueGeneratedOnAdd().HasDefaultValueSql("now()");
    }
}
