namespace EFootwear.Domain.Parties;

/// <summary>The status of a party (docx 3.1: radio Active / In Active). The values mirror the CHECK constraint of party.parties.</summary>
public static class PartyStatusCodes
{
    public const string Active = "ACTIVE";
    public const string Inactive = "INACTIVE";

    public static bool IsValid(string? statusCode) => statusCode is Active or Inactive;
}
