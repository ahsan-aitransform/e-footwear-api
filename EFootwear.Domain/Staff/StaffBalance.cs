namespace EFootwear.Domain.Staff;

/// <summary>
/// What a staff member owes or is owed (advances, salary payable), summed from the journal by a database trigger.
/// Table: contacts.staff_balances. READ ONLY for the application. Balance = DebitTotal - CreditTotal.
/// </summary>
public sealed class StaffBalance
{
    public Guid CompanyId { get; set; }
    public Guid StaffId { get; set; }
    public long DebitTotal { get; set; }
    public long CreditTotal { get; set; }
    public long Balance { get; set; }
    public DateOnly? LastEntryDate { get; set; }
}
