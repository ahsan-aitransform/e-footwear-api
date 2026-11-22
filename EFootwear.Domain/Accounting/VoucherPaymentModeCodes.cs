namespace EFootwear.Domain.Accounting;

/// <summary>How the money moved (<c>acct.vouchers.payment_mode</c>; the "Type" dropdown of the receipt voucher, docx 6.2).</summary>
public static class VoucherPaymentModeCodes
{
    public const string Cash = "CASH";
    public const string Bank = "BANK";
    public const string Cheque = "CHEQUE";
    public const string Online = "ONLINE";
    public const string Other = "OTHER";

    public static readonly IReadOnlyList<string> All = [Cash, Bank, Cheque, Online, Other];

    public static bool IsValid(string? paymentModeCode) => paymentModeCode is Cash or Bank or Cheque or Online or Other;
}
