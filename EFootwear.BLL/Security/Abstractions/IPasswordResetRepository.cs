using EFootwear.BLL.Security.Contracts;

namespace EFootwear.BLL.Security.Abstractions;

/// <summary>BLL contract for password reset tokens (sec.password_reset_tokens through the sec.* functions).</summary>
public interface IPasswordResetRepository
{
    /// <summary>
    /// Creates a single-use token for the user. Returns null for an unknown or disabled user, or when too many were requested
    /// in the last hour (the policy is in sec.auth_policy); the caller must not reveal which.
    /// </summary>
    Task<IssuedPasswordReset?> IssueTokenAsync(Guid companyId, string username, string? clientIpAddress,
                                               CancellationToken cancellationToken);

    /// <summary>How long a token stays valid (sec.auth_policy.reset_token_minutes).</summary>
    Task<int> GetTokenLifetimeMinutesAsync(Guid companyId, CancellationToken cancellationToken);

    /// <summary>Sets the new password and ends all sessions. Throws Unauthorized when the token is invalid, used or expired.</summary>
    Task CompleteAsync(Guid companyId, string tokenSecret, string newPasswordHash, CancellationToken cancellationToken);
}
