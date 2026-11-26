using EFootwear.Domain.Common;
using EFootwear.Domain.Contacts;

namespace EFootwear.Domain.Customers;

/// <summary>
/// A customer. Table: contacts.customers. The name and phone are required by the screens and the API; the table keeps the phone
/// nullable because the walk-in customer "Cash Sale" (code WALKIN) and older rows may have none.
/// The customer NEVER stores a balance: what the customer owes is derived from the journal (contacts.customer_balances is a
/// projection kept by the database). Rows are soft-deleted.
/// </summary>
public sealed class Customer : IHasRowVersion
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }

    /// <summary>Generated automatically (103 plus a serial, for example 10300067).</summary>
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateOnly? BirthDate { get; set; }
    public Guid? CityId { get; set; }

    /// <summary>National identity card number, stored as 12345-1234567-1. Optional.</summary>
    public string? Cnic { get; set; }

    /// <summary>REGULAR or SPECIAL (<see cref="CustomerTypeCodes"/>).</summary>
    public string CustomerType { get; set; } = CustomerTypeCodes.Regular;

    public string Status { get; set; } = ContactStatusCodes.Active;
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public long RowVersion { get; set; }
}
