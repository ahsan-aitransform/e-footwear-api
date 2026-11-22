namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>
/// The printable Voucher Report (docx 6.6): every voucher of one type in a date range. <c>TotalAmount</c> counts posted vouchers only;
/// voided vouchers are listed with their status but never add to the total. <c>IsTruncated</c> is true when more rows matched than the limit.
/// </summary>
public sealed record VoucherReportResponse(
    IReadOnlyList<VoucherListItemResponse> Rows,
    long PostedVoucherCount,
    long VoidVoucherCount,
    long TotalAmount,
    bool IsTruncated);
