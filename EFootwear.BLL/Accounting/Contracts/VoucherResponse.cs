namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>
/// A voucher with its two journal lines and the invoices it settles. <c>RowVersion</c> is the concurrency token that the description
/// edit must send back. A voided voucher keeps all its data and shows who voided it and why.
/// </summary>
public sealed record VoucherResponse(
    Guid Id,
    Guid StoreId,
    string StoreName,
    string VoucherTypeCode,
    long VoucherNumber,
    DateOnly VoucherDate,
    long Amount,
    Guid? PartyId,
    string? PartyName,
    string? PaymentMode,
    string? ReferenceNumber,
    string? Description,
    string StatusCode,
    string? VoidReason,
    DateTimeOffset? VoidedAt,
    DateTimeOffset CreatedAt,
    long RowVersion,
    IReadOnlyList<VoucherLineResponse> Lines,
    IReadOnlyList<VoucherAllocationResponse> Allocations);
