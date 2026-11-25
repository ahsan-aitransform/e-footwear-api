namespace EFootwear.Domain.Accounting;

/// <summary>What the "To" and "From" boxes of a voucher can name: a general ledger account, or a customer, a vendor or a staff member.</summary>
public static class VoucherTargetKindCodes
{
    public const string Account = "ACCOUNT";
    public const string Customer = "CUSTOMER";
    public const string Vendor = "VENDOR";
    public const string Staff = "STAFF";

    public static bool IsValid(string? kindCode) => kindCode is Account or Customer or Vendor or Staff;
}
