namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>One row of the voucher list and of the printable Voucher Report.</summary>
public sealed record VoucherListItemResponse(
    Guid Id,
    Guid StoreId,
    string StoreName,
    string VoucherTypeCode,
    long VoucherNumber,
    DateOnly VoucherDate,
    long Amount,
    string DebitAccountTitle,
    string CreditAccountTitle,
    string? ContactName,
    string? ReferenceNumber,
    string? Description,
    string StatusCode);
