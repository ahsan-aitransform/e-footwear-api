namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>One journal line of a voucher as the View Voucher grid shows it (Code, Title, Debit, Credit, Description).</summary>
public sealed record VoucherLineResponse(
    string AccountCode,
    string AccountTitle,
    Guid? PartyId,
    string? PartyName,
    long DebitAmount,
    long CreditAmount,
    string? Description);
