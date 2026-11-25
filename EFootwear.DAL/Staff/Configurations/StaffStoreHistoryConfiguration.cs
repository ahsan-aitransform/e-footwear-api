using EFootwear.Domain.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Staff.Configurations;

/// <summary>Maps <see cref="StaffStoreHistory"/> to contacts.staff_store_history (rows are only added).</summary>
public sealed class StaffStoreHistoryConfiguration : IEntityTypeConfiguration<StaffStoreHistory>
{
    public void Configure(EntityTypeBuilder<StaffStoreHistory> builder)
    {
        builder.ToTable("staff_store_history", "contacts");
        builder.HasKey(history => new { history.CompanyId, history.Id });
        builder.Property(history => history.Id).ValueGeneratedOnAdd().HasDefaultValueSql("uuidv7()");
        builder.Property(history => history.ChangedAt).ValueGeneratedOnAdd().HasDefaultValueSql("now()");
    }
}
