using EFootwear.BLL.Common;

namespace EFootwear.BLL.Security.Contracts;

/// <summary>Filters of the Users Activity screen: date range, user and kind (data change or sign-in), plus paging and free text.</summary>
public sealed record UserActivityQuery(DateTimeOffset? From, DateTimeOffset? To, Guid? UserId, string? EntryKind, PageRequest Page);
