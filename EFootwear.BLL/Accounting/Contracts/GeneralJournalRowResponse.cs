namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>
/// One journal line of the General Journal (docx 8.28: Id, Store, Title, Date, Type, V #, Description, Debit, Credit). <c>TypeCode</c> is the voucher
/// type for voucher entries (PAYMENT, RECEIPT, EXPENSE, JOURNAL) and the source of the entry otherwise (SALE, PURCHASE, OPENING ...).
/// <c>VoucherNumber</c> is set for voucher entries only.
/// </summary>
public sealed record GeneralJournalRowResponse(
    Guid EntryId,
    DateOnly EntryDate,
    int LineNumber,
    Guid StoreId,
    string StoreName,
    string AccountCode,
    string AccountTitle,
    string? ContactName,
    string TypeCode,
    long? VoucherNumber,
    string? Description,
    long DebitAmount,
    long CreditAmount);
