using EFootwear.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Security.Configurations;

/// <summary>Maps <see cref="UserSession"/> to sec.sessions. Sessions are created and slid forward by the sec.* functions; the API only reads and revokes.</summary>
public sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("sessions", "sec");
        builder.HasKey(session => new { session.CompanyId, session.Id });
        builder.Property(session => session.IpAddress).HasColumnName("ip").HasColumnType("inet");
    }
}
