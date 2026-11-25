namespace EFootwear.Domain.Contacts;

/// <summary>The status of a customer, vendor or staff member (radio Active / In Active). The values mirror the CHECK constraint of the contacts tables.</summary>
public static class ContactStatusCodes
{
    public const string Active = "ACTIVE";
    public const string Inactive = "INACTIVE";

    public static bool IsValid(string? statusCode) => statusCode is Active or Inactive;
}
