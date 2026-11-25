using EFootwear.Domain.Common;
using EFootwear.Domain.Contacts;

namespace EFootwear.Domain.Vendors;

/// <summary>
/// A vendor (supplier). Table: contacts.vendors. The credit limit is whole rupees (null means no limit): the database refuses a purchase
/// bill that takes the open bills above it (purch.check_vendor_credit). The vendor never stores a balance: what the company owes is derived
/// from the journal (contacts.vendor_balances is a projection kept by the database). Rows are soft-deleted.
/// </summary>
public sealed class Vendor : IHasRowVersion
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateOnly? BirthDate { get; set; }
    public Guid? CityId { get; set; }
    public long? CreditLimit { get; set; }
    public string Status { get; set; } = ContactStatusCodes.Active;
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public long RowVersion { get; set; }
}
