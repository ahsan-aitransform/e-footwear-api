using EFootwear.BLL.Security.Contracts;

namespace EFootwear.BLL.Security.Abstractions;

/// <summary>
/// BLL contract for the data layer: everything the company login and session logic needs from the database.
/// Implemented in EFootwear.DAL/Security/Repositories/UserAuthenticationRepository.cs by calling the sec.* functions.
/// </summary>
public interface IUserAuthenticationRepository
{
    /// <summary>Binds the next database transaction to this company (needed before a user exists).</summary>
    void UseCompany(Guid companyId);

    /// <summary>
    /// Finds the company of the user with this email (emails are unique platform-wide). Returns null when
    /// there is no such user. Usable before sign-in.
    /// </summary>
    Task<Guid?> FindCompanyIdByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>Returns the Argon2 hash for the username in the current company, or null if no such user.</summary>
    Task<string?> GetPasswordHashAsync(string username, CancellationToken cancellationToken);

    /// <summary>Returns the Argon2 hash of a user by id, for the change-password check.</summary>
    Task<string?> GetPasswordHashByUserIdAsync(Guid companyId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Calls sec.login: applies lockout, writes login history, creates the session.</summary>
    Task<LoginOutcome> LoginAsync(Guid companyId, string username, bool isPasswordCorrect,
                                  string? clientIpAddress, string? userAgent, CancellationToken cancellationToken);

    /// <summary>
    /// Checks the session (slides the idle timeout) and loads who the user is, including the permissions they hold.
    /// Throws when the session is missing, expired or revoked.
    /// </summary>
    Task<SessionIdentity> ValidateSessionAsync(Guid companyId, string token, CancellationToken cancellationToken);

    Task LogoutAsync(Guid companyId, string token, CancellationToken cancellationToken);

    /// <summary>Calls sec.change_password: ends all sessions of the user.</summary>
    Task ChangePasswordAsync(Guid companyId, Guid userId, string newPasswordHash, CancellationToken cancellationToken);

    /// <summary>Name, email and company name of the signed-in user, for GET /auth/me.</summary>
    Task<CurrentUserProfile> GetCurrentUserProfileAsync(Guid companyId, Guid userId, CancellationToken cancellationToken);
}
