namespace EFootwear.BLL.Commissions.Contracts;

/// <summary>Filters of the commission entries list. An empty store means every store the person may see.</summary>
public sealed record CommissionEntrySearchRequest(Guid? StoreId, Guid? StaffId, string? StatusCode, DateOnly? FromDate, DateOnly? ToDate);
