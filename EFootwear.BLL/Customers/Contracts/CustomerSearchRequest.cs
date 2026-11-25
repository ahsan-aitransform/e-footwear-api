namespace EFootwear.BLL.Customers.Contracts;

/// <summary>
/// The filters of View Customers and Search Customer. The free search text travels in the page request. Bound from the query string next to it.
/// </summary>
/// <param name="CityId">Only customers of this city/area; null means every city.</param>
/// <param name="StatusCode">ACTIVE or INACTIVE; null means both.</param>
public sealed record CustomerSearchRequest(Guid? CityId = null, string? StatusCode = null);
