using EFootwear.Domain.Common;

namespace EFootwear.Domain.Parties;

/// <summary>
/// A customer, vendor and/or staff member. Table: party.parties. One person or business can hold several roles at once
/// (docx 3.1: "A single party record can be customer, vendor and/or staff at the same time").
/// The party NEVER stores a balance: what the party owes or is owed is derived from the journal (party.party_balances is a
/// projection kept by the database, Part 8). Rows are soft-deleted.
/// </summary>
public sealed class Party : IHasRowVersion
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }

    /// <summary>Generated automatically (group 103 plus a serial, for example 10300067).</summary>
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";
    public string? NameUrdu { get; set; }
    public string? FatherName { get; set; }

    /// <summary>Pakistani national identity number, format 12345-1234567-1 (the database checks the shape).</summary>
    public string? Cnic { get; set; }

    public string? PrimaryPhone { get; set; }
    public string? SecondaryPhone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public Guid? CityId { get; set; }
    public bool IsCustomer { get; set; }
    public bool IsVendor { get; set; }
    public bool IsStaff { get; set; }

    /// <summary>Whole rupees; null means no limit.</summary>
    public long? CreditLimit { get; set; }

    public string Status { get; set; } = PartyStatusCodes.Active;

    /// <summary>JSON for the fields the schema has no column for (Urdu father name, Urdu address, notes). Always a JSON object.</summary>
    public string AttributesJson { get; set; } = "{}";

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public long RowVersion { get; set; }
}
