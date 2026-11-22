using EFootwear.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Platform.Configurations;

/// <summary>Maps <see cref="Plan"/> to platform.plans. The application only reads it; writes go through platform.create_plan / update_plan.</summary>
public sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans", "platform");
        builder.HasKey(plan => plan.Code);
        builder.Property(plan => plan.Features).HasColumnType("jsonb");
    }
}
