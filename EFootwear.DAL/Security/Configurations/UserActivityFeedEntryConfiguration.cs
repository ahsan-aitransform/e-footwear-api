using EFootwear.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Security.Configurations;

/// <summary>Maps <see cref="UserActivityFeedEntry"/> to the read-only view audit.v_user_activity_feed.</summary>
public sealed class UserActivityFeedEntryConfiguration : IEntityTypeConfiguration<UserActivityFeedEntry>
{
    public void Configure(EntityTypeBuilder<UserActivityFeedEntry> builder)
    {
        builder.ToView("v_user_activity_feed", "audit");
        builder.HasNoKey();
        builder.Property(entry => entry.IpAddress).HasColumnType("inet");
    }
}
