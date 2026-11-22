using EFootwear.BLL.Common;
using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Security.Repositories;

/// <summary>
/// DAL for the Users Activity feed: reads the view audit.v_user_activity_feed (data changes and sign-in events). The view runs with
/// the caller's row-level security, so only the company's own rows are ever visible. Newest first unless the grid asks otherwise.
/// </summary>
public sealed class UserActivityRepository(DatabaseSession databaseSession) : IUserActivityRepository
{
    private static readonly SortableFields<UserActivityFeedEntry> ActivitySortableFields = new SortableFields<UserActivityFeedEntry>()
        .Allow("occurredAt", entry => entry.OccurredAt)
        .Allow("userFullName", entry => entry.UserFullName)
        .Allow("action", entry => entry.Action)
        .Allow("entityType", entry => entry.EntityType);

    public async Task<PagedResult<UserActivityEntryResponse>> QueryAsync(UserActivityQuery query, IReadOnlyList<Guid>? storeScope,
                                                                         CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var feed = databaseContext.UserActivityFeedEntries.AsNoTracking().AsQueryable();

        if (query.From is { } from) feed = feed.Where(entry => entry.OccurredAt >= from);
        if (query.To is { } to) feed = feed.Where(entry => entry.OccurredAt <= to);
        if (query.UserId is { } userId) feed = feed.Where(entry => entry.UserId == userId);
        if (query.EntryKind is { } entryKind) feed = feed.Where(entry => entry.EntryKind == entryKind);

        if (query.Page.SearchText is { } searchText)
        {
            var pattern = $"%{EscapeLikePattern(searchText)}%";
            feed = feed.Where(entry => EF.Functions.ILike(entry.Action, pattern)
                                       || EF.Functions.ILike(entry.EntityType, pattern)
                                       || (entry.Remarks != null && EF.Functions.ILike(entry.Remarks, pattern))
                                       || (entry.UserFullName != null && EF.Functions.ILike(entry.UserFullName, pattern))
                                       || (entry.UserEmail != null && EF.Functions.ILike(entry.UserEmail, pattern)));
        }

        if (storeScope is not null)
        {
            var scopedUserIds = databaseContext.UserStoreAccesses.AsNoTracking()
                .Where(access => storeScope.Contains(access.StoreId))
                .Select(access => access.UserId);
            feed = feed.Where(entry => (entry.StoreId != null && storeScope.Contains(entry.StoreId.Value))
                                       || (entry.UserId != null && scopedUserIds.Contains(entry.UserId.Value)));
        }

        var totalItemCount = await feed.LongCountAsync(cancellationToken);
        var request = query.Page;
        var ordered = ActivitySortableFields.TryOrder(feed, request.SortField, request.SortDirection, out var sorted)
            ? sorted.ThenByDescending(entry => entry.EntryId)
            : feed.OrderByDescending(entry => entry.OccurredAt).ThenByDescending(entry => entry.EntryId);

        var entries = await ordered.Skip(request.SkippedItemCount).Take(request.PageSize).ToListAsync(cancellationToken);
        var items = entries
            .Select(entry => new UserActivityEntryResponse(entry.EntryId, entry.OccurredAt, entry.UserId, entry.UserFullName, entry.UserEmail,
                entry.EntryKind, entry.Action, entry.EntityType, entry.EntityId, entry.Remarks, entry.DeviceLabel,
                entry.IpAddress?.ToString(), entry.OldValuesJson, entry.NewValuesJson))
            .ToList();
        return new PagedResult<UserActivityEntryResponse>(items, request.PageNumber, request.PageSize, totalItemCount);
    }

    // A user typing % or _ must not turn the search into a wildcard.
    private static string EscapeLikePattern(string text) =>
        text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
