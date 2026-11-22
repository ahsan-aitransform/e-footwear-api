using EFootwear.Domain.Parties;

namespace EFootwear.BLL.Parties.Contracts;

/// <summary>
/// The filters of Manage Party and Search Party (docx 3.2: City/Area and Party Type; 3.3: free search text, which travels in the
/// page request). Bound from the query string next to the page request.
/// </summary>
/// <param name="PartyTypeCode">ALL, CUSTOMER, VENDOR or STAFF (see <see cref="PartyTypeFilterCodes"/>).</param>
/// <param name="CityId">Only parties of this city/area; null means every city.</param>
/// <param name="StatusCode">ACTIVE or INACTIVE; null means both.</param>
public sealed record PartySearchRequest(string PartyTypeCode = PartyTypeFilterCodes.All, Guid? CityId = null, string? StatusCode = null);
