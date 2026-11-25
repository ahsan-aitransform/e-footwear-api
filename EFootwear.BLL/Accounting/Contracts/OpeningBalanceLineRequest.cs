using EFootwear.Domain.Contacts;

namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>A line to save: the account, the customer, the vendor or the staff member (exactly one) and its opening amount on the debit or the credit side. Both zero clears it.</summary>
public sealed record OpeningBalanceLineRequest(Guid? AccountId, Guid? CustomerId, Guid? VendorId, Guid? StaffId, long DebitAmount, long CreditAmount)
{
    /// <summary>The customer, vendor or staff member of the line, or null when the line is an account. Throws when more than one is named.</summary>
    public ContactReference? FindContact() => ContactReference.FromOptionalIds(CustomerId, VendorId, StaffId, "an opening balance line");
}
