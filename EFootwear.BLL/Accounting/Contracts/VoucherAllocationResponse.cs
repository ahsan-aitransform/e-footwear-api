namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>An invoice that a voucher settles. <c>InvoiceKindCode</c> is PURCHASE or SALE.</summary>
public sealed record VoucherAllocationResponse(string InvoiceKindCode, Guid InvoiceId, long InvoiceNumber, long Amount);
