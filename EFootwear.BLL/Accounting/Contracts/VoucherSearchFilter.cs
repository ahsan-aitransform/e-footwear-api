namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>
/// Filters of the voucher list and report. A missing <c>StoreId</c> means "every store the caller may see"; row-level security in the
/// database still limits the result to the caller's stores.
/// </summary>
public sealed record VoucherSearchFilter(
    string? VoucherTypeCode,
    Guid? StoreId,
    DateOnly? FromDate,
    DateOnly? ToDate,
    string? StatusCode,
    Guid? CustomerId = null,
    Guid? VendorId = null,
    Guid? StaffId = null);
