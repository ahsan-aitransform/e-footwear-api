namespace EFootwear.BLL.Payroll.Contracts;

/// <summary>
/// The salary sheet of one store for one month: header, lines and the totals of the sheet. The sheet is a DRAFT until it is approved (approval posts the
/// salary expense); an approved sheet is paid line by line and is never edited. Cancelled lines (kept for history when a draft is rebuilt) are not listed.
/// </summary>
public sealed record PayrollSheetResponse(
    Guid RunId,
    Guid StoreId,
    string StoreName,
    DateOnly PeriodMonth,
    string StatusCode,
    Guid? ApprovedByUserId,
    string? ApprovedByName,
    DateTimeOffset? ApprovedAt,
    long RowVersion,
    IReadOnlyList<PayrollSheetLineResponse> Lines,
    long TotalBaseSalary,
    long TotalCommission,
    long TotalIncentive,
    long TotalDeduction,
    long TotalNetPay,
    long TotalPaid,
    long TotalOutstanding);
