namespace EFootwear.Domain.Staff;

/// <summary>
/// One move of a staff member between stores of the same company (previous store, new store, date, who authorised it).
/// Table: contacts.staff_store_history. Rows are only added.
/// </summary>
public sealed class StaffStoreHistory
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public Guid StaffId { get; set; }
    public Guid? FromStoreId { get; set; }
    public Guid ToStoreId { get; set; }
    public DateOnly EffectiveOn { get; set; }
    public Guid? ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}
