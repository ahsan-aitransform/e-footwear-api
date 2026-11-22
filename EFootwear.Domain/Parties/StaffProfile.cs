using EFootwear.Domain.Common;

namespace EFootwear.Domain.Parties;

/// <summary>Job details of a party that is staff. Table: party.staff_profiles (one row per staff party).</summary>
public sealed class StaffProfile : IHasRowVersion
{
    public Guid CompanyId { get; set; }
    public Guid PartyId { get; set; }
    public string? Designation { get; set; }
    public bool IsManager { get; set; }

    /// <summary>Whole rupees per pay period. Salary terms are managed by the payroll module; new staff start at 0.</summary>
    public long BaseSalary { get; set; }

    public string PayFrequency { get; set; } = "MONTHLY";

    /// <summary>The store the person works in now (never the head office).</summary>
    public Guid CurrentStoreId { get; set; }

    public DateOnly? JoinedOn { get; set; }
    public DateOnly? LeftOn { get; set; }
    public Guid? CreatedBy { get; set; }
    public long RowVersion { get; set; }
}
