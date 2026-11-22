namespace EFootwear.Domain.Accounting;

/// <summary>What the "To" and "From" boxes of a voucher can name: a general ledger account, or a party (customer, vendor, staff).</summary>
public static class VoucherTargetKindCodes
{
    public const string Account = "ACCOUNT";
    public const string Party = "PARTY";
}
