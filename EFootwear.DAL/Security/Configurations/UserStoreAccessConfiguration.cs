using EFootwear.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Security.Configurations;

/// <summary>Maps <see cref="UserStoreAccess"/> to sec.user_stores.</summary>
public sealed class UserStoreAccessConfiguration : IEntityTypeConfiguration<UserStoreAccess>
{
    public void Configure(EntityTypeBuilder<UserStoreAccess> builder)
    {
        builder.ToTable("user_stores", "sec");
        builder.HasKey(access => new { access.CompanyId, access.UserId, access.StoreId });
    }
}
