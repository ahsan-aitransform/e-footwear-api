using EFootwear.DAL.Persistence;
using EFootwear.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Parties.Configurations;

/// <summary>Maps <see cref="StaffProfile"/> to party.staff_profiles.</summary>
public sealed class StaffProfileConfiguration : IEntityTypeConfiguration<StaffProfile>
{
    public void Configure(EntityTypeBuilder<StaffProfile> builder)
    {
        builder.ToTable("staff_profiles", "party");
        builder.HasKey(profile => new { profile.CompanyId, profile.PartyId });
        builder.ConfigureRowVersionConcurrencyToken();
    }
}
