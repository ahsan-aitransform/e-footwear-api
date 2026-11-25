namespace EFootwear.BLL.HumanResources.Contracts;

/// <summary>Filters of the staff transfer list: one staff member, a store (as source or destination) and a date range of the effective date.</summary>
public sealed record StaffTransferSearchRequest(Guid? StaffId, Guid? StoreId, DateOnly? FromDate, DateOnly? ToDate);
