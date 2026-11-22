namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>The "To" or "From" side of a voucher: name exactly one of a general ledger account or a party (customer, vendor, staff).</summary>
public sealed record VoucherTargetRequest(Guid? AccountId, Guid? PartyId);
