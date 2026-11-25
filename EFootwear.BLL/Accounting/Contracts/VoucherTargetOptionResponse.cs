namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>
/// One choice of the "To" / "From" dropdowns. <c>KindCode</c> is ACCOUNT, CUSTOMER, VENDOR or STAFF; exactly one of AccountId, CustomerId, VendorId and StaffId is set. <c>BalanceAmount</c> is the absolute balance in whole
/// rupees and <c>BalanceSideCode</c> its side (DEBIT or CREDIT); both are derived from the journal, never stored.
/// <c>TypeTitle</c> is the group title for an account, or Customer / Vendor / Staff.
/// </summary>
public sealed record VoucherTargetOptionResponse(
    string KindCode,
    Guid? AccountId,
    Guid? CustomerId,
    Guid? VendorId,
    Guid? StaffId,
    string Code,
    string Title,
    string TypeTitle,
    long BalanceAmount,
    string BalanceSideCode);
