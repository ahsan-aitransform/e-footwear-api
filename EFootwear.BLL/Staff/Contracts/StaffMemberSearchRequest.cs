namespace EFootwear.BLL.Staff.Contracts;

/// <summary>The filters of View Staff. The free search text travels in the page request. Bound from the query string next to it.</summary>
/// <param name="CityId">Only staff of this city/area; null means every city.</param>
/// <param name="StoreId">Only staff working in this store; null means every store the caller may use.</param>
/// <param name="StatusCode">ACTIVE or INACTIVE; null means both.</param>
public sealed record StaffMemberSearchRequest(Guid? CityId = null, Guid? StoreId = null, string? StatusCode = null);
