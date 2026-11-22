using System.Net;

namespace EFootwear.Domain.Security;

/// <summary>
/// A sign-in session. Table: sec.sessions. TokenHash identifies the session without exposing the token; it never leaves the data layer.
/// A session is live while it is not revoked, not past its absolute limit and not past its idle limit.
/// </summary>
public sealed class UserSession
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset IdleExpiresAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public IPAddress? IpAddress { get; set; }
    public string? DeviceLabel { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokeReason { get; set; }
}
