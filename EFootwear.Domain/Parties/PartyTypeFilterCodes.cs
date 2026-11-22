namespace EFootwear.Domain.Parties;

/// <summary>The "Party Type" filter of Manage Party (docx 3.2: All, Customer, Vendor, Staff).</summary>
public static class PartyTypeFilterCodes
{
    public const string All = "ALL";
    public const string Customer = "CUSTOMER";
    public const string Vendor = "VENDOR";
    public const string Staff = "STAFF";

    public static bool IsValid(string? filterCode) => filterCode is All or Customer or Vendor or Staff;
}
