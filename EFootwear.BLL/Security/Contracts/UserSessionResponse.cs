namespace EFootwear.BLL.Security.Contracts;

/// <summary>A live sign-in session. The token itself is never returned.</summary>
public sealed record UserSessionResponse(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, DateTimeOffset ExpiresAt,
                                         string? IpAddress, string? UserAgent, string? DeviceLabel, bool IsCurrentSession);
