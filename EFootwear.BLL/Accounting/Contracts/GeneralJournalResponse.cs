using EFootwear.BLL.Common;

namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>A page of the General Journal with the debit and credit totals of the whole selection (they are equal: every entry balances).</summary>
public sealed record GeneralJournalResponse(
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? StoreId,
    string? TypeCode,
    long TotalDebitAmount,
    long TotalCreditAmount,
    PagedResult<GeneralJournalRowResponse> Lines);
