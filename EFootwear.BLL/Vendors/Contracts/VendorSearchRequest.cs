namespace EFootwear.BLL.Vendors.Contracts;

/// <summary>
/// The filters of View Vendors. The free search text travels in the page request. Bound from the query string next to it.
/// </summary>
/// <param name="CityId">Only vendors of this city/area; null means every city.</param>
/// <param name="StatusCode">ACTIVE or INACTIVE; null means both.</param>
public sealed record VendorSearchRequest(Guid? CityId = null, string? StatusCode = null);
