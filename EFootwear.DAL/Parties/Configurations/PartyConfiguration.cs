using EFootwear.DAL.Persistence;
using EFootwear.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Parties.Configurations;

/// <summary>
/// Maps <see cref="Party"/> to party.parties. change_seq (the sync cursor) is maintained by a trigger and is not mapped.
/// The extra attributes are a jsonb column; the application only ever writes a JSON object.
/// </summary>
public sealed class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    public void Configure(EntityTypeBuilder<Party> builder)
    {
        builder.ToTable("parties", "party");
        builder.HasKey(party => new { party.CompanyId, party.Id });
        builder.Property(party => party.Id).ValueGeneratedOnAdd().HasDefaultValueSql("uuidv7()");
        builder.Property(party => party.NameUrdu).HasColumnName("name_ur");
        builder.Property(party => party.PrimaryPhone).HasColumnName("phone_primary");
        builder.Property(party => party.SecondaryPhone).HasColumnName("phone_secondary");
        builder.Property(party => party.Status).HasColumnName("status");
        builder.Property(party => party.AttributesJson).HasColumnName("attributes").HasColumnType("jsonb");
        builder.Property(party => party.CreatedAt).ValueGeneratedOnAdd().HasDefaultValueSql("now()");
        builder.ConfigureRowVersionConcurrencyToken();
    }
}
