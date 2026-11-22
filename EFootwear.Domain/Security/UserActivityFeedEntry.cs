using System.Net;

namespace EFootwear.Domain.Security;

/// <summary>
/// One line of the Users Activity feed: either a data change (audit.activity_log) or a sign-in event (sec.login_attempts).
/// Read-only: it is mapped to the view audit.v_user_activity_feed, which runs with the caller's row-level security.
/// </summary>
public sealed class UserActivityFeedEntry
{
    public Guid CompanyId { get; set; }
    public Guid EntryId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? UserId { get; set; }
    public string? UserFullName { get; set; }
    public string? UserEmail { get; set; }
    public Guid? StoreId { get; set; }
    public string EntryKind { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string? EntityId { get; set; }
    public string? Remarks { get; set; }
    public IPAddress? IpAddress { get; set; }
    public string? DeviceLabel { get; set; }
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
}
