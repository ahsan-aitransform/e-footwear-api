using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Security.Services;

/// <summary>
/// Session management and the login history (Part 6). Everybody sees and ends their OWN sessions without a permission (a person must
/// always be able to sign out of a lost device). Seeing or ending the sessions of ANOTHER user, and reading others' sign-in history,
/// needs the screen permissions and the management rules (<see cref="UserManagementRules"/>). The secret of the current session is used
/// only to mark it and to keep it when "end all other sessions" is chosen; it is never stored or returned.
/// </summary>
public sealed class UserSessionService(
    UserManagementService userManagementService,
    IUserRepository userRepository,
    IUserSessionRepository sessionRepository,
    IActivityLogWriter activityLogWriter,
    IDateTimeProvider dateTimeProvider,
    RequestContext requestContext,
    IUnitOfWork unitOfWork,
    ILogger<UserSessionService> logger)
{
    public const string SessionEndedByUserReason = "USER_REVOKED";
    public const string SessionEndedByAdministratorReason = "ADMIN_REVOKED";
    public const int DefaultHistoryDays = 30;
    public const int MaximumHistoryDays = 366;

    public Task<IReadOnlyList<UserSessionResponse>> ListOwnSessionsAsync(string? currentSessionSecret, CancellationToken cancellationToken) =>
        sessionRepository.ListLiveSessionsAsync(requestContext.CompanyId, requestContext.UserId, currentSessionSecret, cancellationToken);

    public async Task EndOwnSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var ended = await sessionRepository.RevokeAsync(requestContext.CompanyId, requestContext.UserId, sessionId, null,
                                                        SessionEndedByUserReason, cancellationToken);
        if (ended == 0) throw new DomainException(ErrorKind.NotFound, "That session is not active.");
        await unitOfWork.CommitAsync(cancellationToken);
    }

    /// <summary>Ends every session of the caller except the one in use. Returns how many were ended.</summary>
    public async Task<int> EndOtherOwnSessionsAsync(string? currentSessionSecret, CancellationToken cancellationToken)
    {
        var ended = await sessionRepository.RevokeAsync(requestContext.CompanyId, requestContext.UserId, null, currentSessionSecret,
                                                        SessionEndedByUserReason, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return ended;
    }

    public async Task<IReadOnlyList<UserSessionResponse>> ListSessionsOfUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await userManagementService.FindManageableUserAsync(userId, UserManagementAction.ManageSessions, cancellationToken);
        return await sessionRepository.ListLiveSessionsAsync(requestContext.CompanyId, userId, null, cancellationToken);
    }

    public async Task EndSessionOfUserAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        await userManagementService.FindManageableUserAsync(userId, UserManagementAction.ManageSessions, cancellationToken);
        var ended = await sessionRepository.RevokeAsync(requestContext.CompanyId, userId, sessionId, null,
                                                        SessionEndedByAdministratorReason, cancellationToken);
        if (ended == 0) throw new DomainException(ErrorKind.NotFound, "That session is not active.");

        await activityLogWriter.WriteAsync(new ActivityLogEntryRecord(requestContext.CompanyId, SecurityActivityActions.SessionRevoked,
            SecurityActivityActions.UserSessionsEntityType, sessionId.ToString(), null, new { userId },
            "Session ended by an administrator"), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Session {SessionId} of user {UserId} ended by {ActorUserId}", sessionId, userId, requestContext.UserId);
    }

    public Task<IReadOnlyList<LoginHistoryEntryResponse>> ListOwnLoginHistoryAsync(DateTimeOffset? from, DateTimeOffset? to,
                                                                                   CancellationToken cancellationToken)
    {
        var (rangeStart, rangeEnd) = NormalizeRange(from, to);
        return sessionRepository.ListLoginHistoryAsync(requestContext.CompanyId, requestContext.UserId, rangeStart, rangeEnd, cancellationToken);
    }

    /// <summary>
    /// Sign-in history of one user, or of everyone the caller may see when <paramref name="userId"/> is empty (a store manager sees the
    /// people of its own stores only).
    /// </summary>
    public async Task<IReadOnlyList<LoginHistoryEntryResponse>> ListLoginHistoryAsync(Guid? userId, DateTimeOffset? from, DateTimeOffset? to,
                                                                                     CancellationToken cancellationToken)
    {
        var (rangeStart, rangeEnd) = NormalizeRange(from, to);
        if (userId is { } singleUserId)
        {
            await userManagementService.FindManageableUserAsync(singleUserId, UserManagementAction.View, cancellationToken);
            return await sessionRepository.ListLoginHistoryAsync(requestContext.CompanyId, singleUserId, rangeStart, rangeEnd, cancellationToken);
        }

        var everything = await sessionRepository.ListLoginHistoryAsync(requestContext.CompanyId, null, rangeStart, rangeEnd, cancellationToken);
        if (requestContext.HasAccessToAllStores) return everything;

        var visibleUsers = await userRepository.ListAsync(requestContext.CompanyId, requestContext.AssignedStoreIds.ToList(), cancellationToken);
        var visibleUserIds = visibleUsers.Select(user => user.Id).Append(requestContext.UserId).ToHashSet();
        return everything.Where(entry => entry.UserId is { } entryUserId && visibleUserIds.Contains(entryUserId)).ToList();
    }

    private (DateTimeOffset From, DateTimeOffset To) NormalizeRange(DateTimeOffset? from, DateTimeOffset? to)
    {
        var rangeEnd = (to ?? dateTimeProvider.UtcNow).ToUniversalTime();
        var rangeStart = (from ?? rangeEnd.AddDays(-DefaultHistoryDays)).ToUniversalTime();
        if (rangeStart > rangeEnd)
            throw new DomainException(ErrorKind.Validation, "The start date must not be after the end date.");
        if (rangeEnd - rangeStart > TimeSpan.FromDays(MaximumHistoryDays))
            throw new DomainException(ErrorKind.Validation, $"Choose a range of at most {MaximumHistoryDays} days.");
        return (rangeStart, rangeEnd);
    }
}
