namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>
/// One voucher of any type (the type comes from the endpoint). <c>DebitTarget</c> is the "To" box (بنام Dr) and <c>CreditTarget</c> the
/// "From" box (جمع Cr) of the docx. <c>Amount</c> is in whole rupees. <c>ClientTransactionId</c> makes a retry safe: sending the same
/// Guid again returns the voucher that was posted the first time instead of posting a second one.
/// </summary>
public sealed record PostVoucherRequest(
    Guid StoreId,
    DateOnly VoucherDate,
    VoucherTargetRequest DebitTarget,
    VoucherTargetRequest CreditTarget,
    long Amount,
    string? PaymentMode,
    string? ReferenceNumber,
    string? Description,
    IReadOnlyList<VoucherAllocationRequest>? Allocations,
    Guid? ClientTransactionId);
