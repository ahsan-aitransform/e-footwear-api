using EFootwear.Domain.Contacts;

namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>The "To" or "From" side of a voucher: name exactly one of a general ledger account, a customer, a vendor or a staff member.</summary>
public sealed record VoucherTargetRequest(Guid? AccountId, Guid? CustomerId, Guid? VendorId, Guid? StaffId)
{
    /// <summary>The customer, vendor or staff member named on this side, or null when an account is named. Throws when more than one is named.</summary>
    public ContactReference? FindContact(string sideTitle) => ContactReference.FromOptionalIds(CustomerId, VendorId, StaffId, sideTitle);

    public bool NamesContact => CustomerId.HasValue || VendorId.HasValue || StaffId.HasValue;
}
