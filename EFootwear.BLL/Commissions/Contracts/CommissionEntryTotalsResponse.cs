namespace EFootwear.BLL.Commissions.Contracts;

/// <summary>The commission of every entry that matches the filters (not only the page shown), split by where it stands. Cancelled entries are left out.</summary>
public sealed record CommissionEntryTotalsResponse(long PendingAmount, long ApprovedAmount, long PaidAmount, long TotalAmount, long EntryCount);
