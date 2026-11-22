namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>
/// One choice of the "To" / "From" dropdowns. <c>KindCode</c> is ACCOUNT or PARTY. <c>BalanceAmount</c> is the absolute balance in whole
/// rupees and <c>BalanceSideCode</c> its side (DEBIT or CREDIT); both are derived from the journal, never stored.
/// <c>TypeTitle</c> is the group title for an account, or Customer / Vendor / Staff for a party.
/// </summary>
public sealed record VoucherTargetOptionResponse(
    string KindCode,
    Guid? AccountId,
    Guid? PartyId,
    string Code,
    string Title,
    string TypeTitle,
    long BalanceAmount,
    string BalanceSideCode);
