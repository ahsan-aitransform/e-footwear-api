namespace EFootwear.Domain.Accounting;

/// <summary>Status of a voucher (<c>acct.vouchers.status</c>). A voided voucher keeps its row; its journal entry is reversed, never deleted.</summary>
public static class VoucherStatusCodes
{
    public const string Posted = "POSTED";
    public const string Void = "VOID";

    public static bool IsValid(string? statusCode) => statusCode is Posted or Void;
}
