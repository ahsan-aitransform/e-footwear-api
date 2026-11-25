namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>
/// One line of an account ledger. <c>RunningBalanceAmount</c> is the absolute running balance (opening balance included) and
/// <c>RunningBalanceSideCode</c> its side. <c>Description</c> is null when the caller asked for "No Description".
/// </summary>
public sealed record AccountLedgerRowResponse(
    DateOnly EntryDate,
    Guid EntryId,
    string SourceTypeCode,
    string? Description,
    string? ContactKindCode,
    Guid? CustomerId,
    Guid? VendorId,
    Guid? StaffId,
    long DebitAmount,
    long CreditAmount,
    long RunningBalanceAmount,
    string RunningBalanceSideCode);
