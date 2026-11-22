namespace EFootwear.Domain.Accounting;

/// <summary>
/// Which side a balance is on, and the "Amount Type" filter of the Account Balance report (docx 8.2: Debit or Credit; the new system
/// also offers All). A balance is always derived from journal lines: debit total minus credit total.
/// </summary>
public static class BalanceSideCodes
{
    public const string Debit = "DEBIT";
    public const string Credit = "CREDIT";
    public const string All = "ALL";

    public static bool IsValidFilter(string? sideCode) => sideCode is Debit or Credit or All;

    /// <summary>The side of a signed balance (debit minus credit). A zero balance is reported as debit with amount zero.</summary>
    public static string SideOf(long debitMinusCredit) => debitMinusCredit >= 0 ? Debit : Credit;
}
