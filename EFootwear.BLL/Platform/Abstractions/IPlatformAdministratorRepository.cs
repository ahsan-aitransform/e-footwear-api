using EFootwear.BLL.Platform.Contracts;

namespace EFootwear.BLL.Platform.Abstractions;

/// <summary>
/// BLL contract for platform administrator accounts. These accounts have no company, so this repository
/// does not use the company-scoped session. Implemented in EFootwear.DAL/Platform/Repositories.
/// </summary>
public interface IPlatformAdministratorRepository
{
    /// <summary>Returns the Argon2 hash for the administrator username, or null if there is none.</summary>
    Task<string?> GetPasswordHashAsync(string username, CancellationToken cancellationToken);

    /// <summary>Calls platform.admin_login: lockout, login history and the session token.</summary>
    Task<PlatformLoginOutcome> LoginAsync(string username, bool isPasswordCorrect, string? clientIpAddress,
                                          string? userAgent, CancellationToken cancellationToken);

    /// <summary>Checks an administrator session. Throws when it is missing, expired or revoked.</summary>
    Task<PlatformAdministratorIdentity> ValidateSessionAsync(string token, CancellationToken cancellationToken);

    Task LogoutAsync(string token, CancellationToken cancellationToken);
}
