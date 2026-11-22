using EFootwear.BLL.Common;
using EFootwear.BLL.Security.Contracts;

namespace EFootwear.BLL.Security.Abstractions;

/// <summary>BLL contract for reading the Users Activity feed (audit.v_user_activity_feed).</summary>
public interface IUserActivityRepository
{
    /// <summary>
    /// One page of the feed, newest first. With <paramref name="storeScope"/> set (a user who does not see every store), only entries of
    /// users who work in one of those stores, or recorded for one of those stores.
    /// </summary>
    Task<PagedResult<UserActivityEntryResponse>> QueryAsync(UserActivityQuery query, IReadOnlyList<Guid>? storeScope,
                                                            CancellationToken cancellationToken);
}
