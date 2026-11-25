using EFootwear.DAL.Persistence;
using EFootwear.Domain.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Staff.Configurations;

/// <summary>Maps <see cref="StaffMember"/> to contacts.staff. change_seq (the sync cursor) is maintained by a trigger and is not mapped.</summary>
public sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    public void Configure(EntityTypeBuilder<StaffMember> builder)
    {
        builder.ToTable("staff", "contacts");
        builder.HasKey(staff => new { staff.CompanyId, staff.Id });
        builder.Property(staff => staff.Id).ValueGeneratedOnAdd().HasDefaultValueSql("uuidv7()");
        builder.Property(staff => staff.Status).HasColumnName("status");
        builder.Property(staff => staff.CreatedAt).ValueGeneratedOnAdd().HasDefaultValueSql("now()");
        builder.ConfigureRowVersionConcurrencyToken();
    }
}
