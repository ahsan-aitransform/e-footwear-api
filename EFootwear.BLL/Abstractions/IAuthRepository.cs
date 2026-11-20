namespace EFootwear.BLL.Abstractions;

// Result of sec.login: Outcome is "OK", "INVALID" or "LOCKED".
public sealed record LoginOutcome(string Outcome, Guid? UserId, string? SessionToken,
                                  bool MustChangePassword, DateTimeOffset? LockedUntil);

// Who the validated company-user session belongs to. Filled from the database, never from the request.
public sealed record SessionIdentity(Guid UserId, string RoleCode, bool AllStores, IReadOnlyList<Guid> StoreIds,
                                     bool MustChangePassword);

/// <summary>
/// BLL contract for the DAL: everything the company login and session logic needs from the database.
/// Implemented in EFootwear.DAL/Auth/AuthRepository.cs by calling the sec.* functions.
/// </summary>
public interface IAuthRepository
{
    /// <summary>Binds the next database transaction to this company (needed before a user exists).</summary>
    void UseCompany(Guid companyId);

    /// <summary>
    /// Finds the company of the user with this email (emails are unique platform-wide). Returns null when
    /// there is no such user. Usable before sign-in.
    /// </summary>
    Task<Guid?> FindCompanyIdByEmailAsync(string email, CancellationToken ct);

    /// <summary>Returns the Argon2 hash for the username in the current company, or null if no such user.</summary>
    Task<string?> GetPasswordHashAsync(string username, CancellationToken ct);

    /// <summary>Returns the Argon2 hash of a user by id, for the change-password check.</summary>
    Task<string?> GetPasswordHashByUserIdAsync(Guid companyId, Guid userId, CancellationToken ct);

    /// <summary>Calls sec.login: applies lockout, writes login history, creates the session.</summary>
    Task<LoginOutcome> LoginAsync(Guid companyId, string username, bool passwordOk,
                                  string? ip, string? userAgent, CancellationToken ct);

    /// <summary>Checks the session (slides idle timeout) and loads who the user is. Throws if invalid.</summary>
    Task<SessionIdentity> ValidateSessionAsync(Guid companyId, string token, CancellationToken ct);

    Task LogoutAsync(Guid companyId, string token, CancellationToken ct);

    /// <summary>Calls sec.change_password: ends all sessions of the user.</summary>
    Task ChangePasswordAsync(Guid companyId, Guid userId, string newPasswordHash, CancellationToken ct);

    /// <summary>Throws a Forbidden DomainException when the current user lacks the permission.</summary>
    Task RequirePermissionAsync(string permissionCode, CancellationToken ct);
}
