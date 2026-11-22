namespace EFootwear.Domain.Accounting;

/// <summary>
/// The four voucher types of the docx (screens 6.1 to 6.4). The values mirror <c>acct.vouchers.voucher_type</c>, and
/// <c>acct.post_voucher</c> builds the number series name from them (<c>PAYMENT</c> + <c>_VOUCHER</c> = PAYMENT_VOUCHER).
/// </summary>
public static class VoucherTypeCodes
{
    public const string Payment = "PAYMENT";
    public const string Receipt = "RECEIPT";
    public const string Expense = "EXPENSE";
    public const string Journal = "JOURNAL";

    public static readonly IReadOnlyList<string> All = [Payment, Receipt, Expense, Journal];

    public static bool IsValid(string? voucherTypeCode) => voucherTypeCode is Payment or Receipt or Expense or Journal;

    /// <summary>Payments to a vendor and receipts from a customer may settle invoices (voucher allocations).</summary>
    public static bool CanSettleInvoices(string voucherTypeCode) => voucherTypeCode is Payment or Receipt;
}
