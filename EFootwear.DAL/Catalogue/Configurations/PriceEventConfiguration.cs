using EFootwear.Domain.Catalogue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Catalogue.Configurations;

/// <summary>Maps <see cref="PriceEvent"/> to catalog.price_events (append-only: the application role has no UPDATE or DELETE right).</summary>
public sealed class PriceEventConfiguration : IEntityTypeConfiguration<PriceEvent>
{
    public void Configure(EntityTypeBuilder<PriceEvent> builder)
    {
        builder.ToTable("price_events", "catalog");
        builder.HasKey(priceEvent => new { priceEvent.CompanyId, priceEvent.Id });
        builder.Property(priceEvent => priceEvent.Id).ValueGeneratedOnAdd().HasDefaultValueSql("uuidv7()");
        builder.Property(priceEvent => priceEvent.ChangedAt).ValueGeneratedOnAdd().HasDefaultValueSql("now()");
    }
}
