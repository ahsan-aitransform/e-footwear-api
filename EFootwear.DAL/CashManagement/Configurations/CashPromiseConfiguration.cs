using EFootwear.DAL.Persistence;
using EFootwear.Domain.CashManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.CashManagement.Configurations;

/// <summary>Maps <see cref="CashPromise"/> to cash.promises.</summary>
public sealed class CashPromiseConfiguration : IEntityTypeConfiguration<CashPromise>
{
    public void Configure(EntityTypeBuilder<CashPromise> builder)
    {
        builder.ToTable("promises", "cash");
        builder.HasKey(promise => new { promise.CompanyId, promise.Id });
        builder.Property(promise => promise.Id).ValueGeneratedOnAdd().HasDefaultValueSql("uuidv7()");
        builder.Property(promise => promise.PromiseNumber).HasColumnName("promise_no");
        builder.Property(promise => promise.Status).HasColumnName("status");
        builder.ConfigureRowVersionConcurrencyToken();
    }
}
