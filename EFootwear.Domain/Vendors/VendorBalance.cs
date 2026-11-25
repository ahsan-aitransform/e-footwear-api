namespace EFootwear.Domain.Vendors;

/// <summary>
/// What the company owes a vendor, summed from the journal by a database trigger. Table: contacts.vendor_balances. READ ONLY for the
/// application. Balance = DebitTotal - CreditTotal; negative means the company owes the vendor.
/// </summary>
public sealed class VendorBalance
{
    public Guid CompanyId { get; set; }
    public Guid VendorId { get; set; }
    public long DebitTotal { get; set; }
    public long CreditTotal { get; set; }
    public long Balance { get; set; }
    public DateOnly? LastEntryDate { get; set; }
}
