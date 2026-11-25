namespace EFootwear.BLL.Staff.Contracts;

/// <summary>The filters of Staff Account Detail: the staff member, From and To. A null staff member means all staff. A null date is open-ended.</summary>
public sealed record StaffLedgerQuery(Guid? StaffId = null, DateOnly? From = null, DateOnly? To = null);
