using EFootwear.Domain.Common;
using EFootwear.Domain.Contacts;

namespace EFootwear.Domain.Staff;

/// <summary>
/// A staff member with the job details. Table: contacts.staff (the person and the job details in one table). Salary terms are
/// managed by the payroll module; new staff start at 0 and every change of the salary or pay frequency writes a salary-term row
/// (database trigger). The staff member never stores a balance: it is derived from the journal (contacts.staff_balances).
/// </summary>
public sealed class StaffMember : IHasRowVersion
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateOnly? BirthDate { get; set; }
    public Guid? CityId { get; set; }
    public string? Designation { get; set; }
    public bool IsManager { get; set; }

    /// <summary>Whole rupees per pay period.</summary>
    public long BaseSalary { get; set; }

    public string PayFrequency { get; set; } = "MONTHLY";

    /// <summary>The store the person works in now (never the head office).</summary>
    public Guid CurrentStoreId { get; set; }

    public DateOnly? JoinedOn { get; set; }
    public DateOnly? LeftOn { get; set; }
    public string Status { get; set; } = ContactStatusCodes.Active;
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public long RowVersion { get; set; }
}
