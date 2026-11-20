using EFootwear.BLL.Common;

namespace EFootwear.BLL.Abstractions;

// Result of platform.admin_login: Outcome is "OK", "INVALID" or "LOCKED".
public sealed record PlatformLoginOutcome(string Outcome, Guid? AdminId, string? SessionToken, DateTimeOffset? LockedUntil);

/// <summary>
/// BLL contract for platform administrator accounts. These accounts have no company, so this repository
/// does not use the company-scoped session. Implemented in EFootwear.DAL/Platform.
/// </summary>
public interface IPlatformAdminRepository
{
    /// <summary>Returns the Argon2 hash for the administrator username, or null if there is none.</summary>
    Task<string?> GetPasswordHashAsync(string username, CancellationToken ct);

    /// <summary>Calls platform.admin_login: lockout, login history and the session token.</summary>
    Task<PlatformLoginOutcome> LoginAsync(string username, bool passwordOk, string? ip, string? userAgent, CancellationToken ct);

    /// <summary>Checks an administrator session. Throws when it is missing, expired or revoked.</summary>
    Task<PlatformAdminIdentity> ValidateSessionAsync(string token, CancellationToken ct);

    Task LogoutAsync(string token, CancellationToken ct);
}
