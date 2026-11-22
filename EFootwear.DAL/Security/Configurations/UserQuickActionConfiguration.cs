using EFootwear.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Security.Configurations;

/// <summary>Maps <see cref="UserQuickAction"/> to sec.user_quick_actions.</summary>
public sealed class UserQuickActionConfiguration : IEntityTypeConfiguration<UserQuickAction>
{
    public void Configure(EntityTypeBuilder<UserQuickAction> builder)
    {
        builder.ToTable("user_quick_actions", "sec");
        builder.HasKey(quickAction => new { quickAction.CompanyId, quickAction.UserId, quickAction.ActionCode });
    }
}
