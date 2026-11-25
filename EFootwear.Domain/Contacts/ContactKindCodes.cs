namespace EFootwear.Domain.Contacts;

/// <summary>
/// The three kinds of person or business the books can name: a customer, a vendor or a staff member. The codes are what the SQL
/// posting functions and the ledger screens use to say which table an id belongs to (contacts.customers, contacts.vendors, contacts.staff).
/// </summary>
public static class ContactKindCodes
{
    public const string Customer = "CUSTOMER";
    public const string Vendor = "VENDOR";
    public const string Staff = "STAFF";

    public static bool IsValid(string? kindCode) => kindCode is Customer or Vendor or Staff;

    public static string TitleOf(string kindCode) => kindCode switch
    {
        Customer => "Customer",
        Vendor => "Vendor",
        Staff => "Staff",
        _ => kindCode,
    };
}
