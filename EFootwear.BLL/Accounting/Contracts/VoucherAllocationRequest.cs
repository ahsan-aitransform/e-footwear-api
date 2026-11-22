namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>
/// Part of a voucher that settles one invoice: a purchase invoice for a payment to a vendor, a credit sale invoice for a receipt from
/// a customer. The part of the voucher amount that is not allocated stays on the party's account.
/// </summary>
public sealed record VoucherAllocationRequest(Guid InvoiceId, long Amount);
