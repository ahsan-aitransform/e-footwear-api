namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>
/// The balance of one account, customer, vendor or staff member as on a date, split into the debit and credit columns of the printed list. Exactly one of
/// <c>DebitAmount</c> and <c>CreditAmount</c> is non-zero unless the balance is zero.
/// </summary>
public sealed record AccountBalanceRowResponse(
    Guid? AccountId,
    Guid? CustomerId,
    Guid? VendorId,
    Guid? StaffId,
    string Code,
    string Title,
    string TypeTitle,
    long DebitAmount,
    long CreditAmount);
