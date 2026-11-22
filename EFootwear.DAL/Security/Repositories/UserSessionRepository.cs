using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Contracts;
using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Security.Repositories;

/// <summary>
/// DAL for sign-in sessions and the login history. Sessions are created and slid forward by the sec.* functions; here they are only
/// read and revoked. The token hash is used to recognise the caller's own session and is never returned.
/// </summary>
public sealed class UserSessionRepository(DatabaseSession databaseSession, IDateTimeProvider dateTimeProvider) : IUserSessionRepository
{
    public async Task<IReadOnlyList<UserSessionResponse>> ListLiveSessionsAsync(Guid companyId, Guid userId, string? currentSessionSecret,
                                                                                CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var now = dateTimeProvider.UtcNow;
        var currentHash = currentSessionSecret is null ? null : SessionTokenHasher.Hash(currentSessionSecret);

        var sessions = await databaseContext.UserSessions.AsNoTracking()
            .Where(session => session.CompanyId == companyId && session.UserId == userId && session.RevokedAt == null
                              && session.ExpiresAt > now && session.IdleExpiresAt > now)
            .OrderByDescending(session => session.CreatedAt)
            .ToListAsync(cancellationToken);
        return sessions
            .Select(session => new UserSessionResponse(session.Id, session.CreatedAt, session.LastSeenAt, session.ExpiresAt,
                session.IpAddress?.ToString(), session.UserAgent, session.DeviceLabel,
                IsCurrentSession: currentHash is not null && session.TokenHash == currentHash))
            .ToList();
    }

    public async Task<int> RevokeAsync(Guid companyId, Guid userId, Guid? sessionId, string? exceptSessionSecret, string reason,
                                       CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var now = dateTimeProvider.UtcNow;
        var exceptHash = exceptSessionSecret is null ? null : SessionTokenHasher.Hash(exceptSessionSecret);

        return await databaseContext.UserSessions
            .Where(session => session.CompanyId == companyId && session.UserId == userId && session.RevokedAt == null
                              && (sessionId == null || session.Id == sessionId)
                              && (exceptHash == null || session.TokenHash != exceptHash))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(session => session.RevokedAt, now)
                .SetProperty(session => session.RevokeReason, reason), cancellationToken);
    }

    public async Task<IReadOnlyList<LoginHistoryEntryResponse>> ListLoginHistoryAsync(Guid companyId, Guid? userId, DateTimeOffset from,
                                                                                      DateTimeOffset to, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        // sec.login_history lets a user read their own history and checks LOGIN_HISTORY_VIEW for anybody else's.
        var rows = await databaseContext.Database.SqlQuery<LoginHistoryRow>($"""
            select out_attempt_id as "AttemptId", out_user_id as "UserId", out_username as "UsernameEntered",
                   out_outcome as "Outcome", out_attempted_at as "AttemptedAt", out_ip::text as "IpAddress",
                   out_device as "DeviceLabel", out_acted_by as "ActedByUserId"
              from sec.login_history({companyId}, {userId}::uuid, {from}, {to})
            """).ToListAsync(cancellationToken);
        return rows
            .Select(row => new LoginHistoryEntryResponse(row.AttemptId, row.UserId, row.UsernameEntered, row.Outcome, row.AttemptedAt,
                                                         row.IpAddress, row.DeviceLabel, row.ActedByUserId))
            .ToList();
    }

    private sealed class LoginHistoryRow
    {
        public Guid AttemptId { get; set; }
        public Guid? UserId { get; set; }
        public string? UsernameEntered { get; set; }
        public string Outcome { get; set; } = "";
        public DateTimeOffset AttemptedAt { get; set; }
        public string? IpAddress { get; set; }
        public string? DeviceLabel { get; set; }
        public Guid? ActedByUserId { get; set; }
    }
}
