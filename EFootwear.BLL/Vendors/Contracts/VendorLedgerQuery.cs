namespace EFootwear.BLL.Vendors.Contracts;

/// <summary>The filters of Vendor Account Detail: the vendor, From and To. A null vendor means all vendors. A null date is open-ended.</summary>
public sealed record VendorLedgerQuery(Guid? VendorId = null, DateOnly? From = null, DateOnly? To = null);
