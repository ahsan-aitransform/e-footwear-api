using EFootwear.BLL.Security.Contracts;

namespace EFootwear.BLL.Security.Abstractions;

/// <summary>BLL contract for sign-in sessions (sec.sessions) and the login history (sec.login_history).</summary>
public interface IUserSessionRepository
{
    /// <summary>Live sessions of a user, newest first. <paramref name="currentSessionSecret"/> marks which one the caller is using.</summary>
    Task<IReadOnlyList<UserSessionResponse>> ListLiveSessionsAsync(Guid companyId, Guid userId, string? currentSessionSecret,
                                                                   CancellationToken cancellationToken);

    /// <summary>
    /// Revokes one live session of the user (<paramref name="sessionId"/>) or, when it is null, every live session except the one the
    /// secret belongs to. Returns how many were revoked.
    /// </summary>
    Task<int> RevokeAsync(Guid companyId, Guid userId, Guid? sessionId, string? exceptSessionSecret, string reason,
                          CancellationToken cancellationToken);

    /// <summary>Sign-in events newest first. A user always sees their own; seeing others needs LOGIN_HISTORY_VIEW (checked in SQL).</summary>
    Task<IReadOnlyList<LoginHistoryEntryResponse>> ListLoginHistoryAsync(Guid companyId, Guid? userId, DateTimeOffset from,
                                                                         DateTimeOffset to, CancellationToken cancellationToken);
}
