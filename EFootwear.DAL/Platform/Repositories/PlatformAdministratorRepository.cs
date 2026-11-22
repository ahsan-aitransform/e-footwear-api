using EFootwear.BLL.Platform.Abstractions;
using EFootwear.BLL.Platform.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PostgresErrors = Npgsql.PostgresErrorCodes;

namespace EFootwear.DAL.Platform.Repositories;

/// <summary>
/// DAL for platform administrator accounts (EF Core). These accounts have no company, so this repository uses its
/// own short-lived context and transaction instead of the company-scoped DatabaseSession.
/// Each method commits its own work (a failed sign-in is recorded too, so lockout works).
/// </summary>
public sealed class PlatformAdministratorRepository(IDbContextFactory<ApplicationDatabaseContext> databaseContextFactory)
    : IPlatformAdministratorRepository
{
    public async Task<string?> GetPasswordHashAsync(string username, CancellationToken cancellationToken)
    {
        await using var databaseContext = await databaseContextFactory.CreateDbContextAsync(cancellationToken);
        return await databaseContext.Database
            .SqlQuery<string>($"select password_hash as \"Value\" from platform.admins where lower(username) = lower({username})")
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PlatformLoginOutcome> LoginAsync(string username, bool isPasswordCorrect, string? clientIpAddress,
                                                       string? userAgent, CancellationToken cancellationToken)
    {
        await using var databaseContext = await databaseContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await databaseContext.Database.BeginTransactionAsync(cancellationToken);
        var row = await databaseContext.Database.SqlQuery<LoginRow>($"""
            select out_outcome as "Outcome", out_admin_id as "AdministratorId", out_session_token as "SessionToken",
                   out_locked_until as "LockedUntil"
              from platform.admin_login({username}, {isPasswordCorrect}, {clientIpAddress}::inet, {userAgent})
            """).SingleAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PlatformLoginOutcome(row.Outcome, row.AdministratorId, row.SessionToken, row.LockedUntil);
    }

    public async Task<PlatformAdministratorIdentity> ValidateSessionAsync(string token, CancellationToken cancellationToken)
    {
        await using var databaseContext = await databaseContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await databaseContext.Database.BeginTransactionAsync(cancellationToken);
        SessionRow row;
        try
        {
            row = await databaseContext.Database.SqlQuery<SessionRow>($"""
                select out_admin_id as "AdministratorId", out_username as "Username", out_full_name as "FullName"
                  from platform.admin_session({token})
                """).SingleAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrors.InvalidAuthorizationSpecification)
        {
            throw new DomainException(ErrorKind.Unauthorized, "Session is missing, expired or revoked.");
        }
        await transaction.CommitAsync(cancellationToken);   // saves the slid idle timeout
        return new PlatformAdministratorIdentity(row.AdministratorId, row.Username, row.FullName);
    }

    public async Task LogoutAsync(string token, CancellationToken cancellationToken)
    {
        await using var databaseContext = await databaseContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await databaseContext.Database.BeginTransactionAsync(cancellationToken);
        await databaseContext.Database.ExecuteSqlAsync($"select platform.admin_logout({token})", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private sealed class LoginRow
    {
        public string Outcome { get; set; } = "";
        public Guid? AdministratorId { get; set; }
        public string? SessionToken { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
    }

    private sealed class SessionRow
    {
        public Guid AdministratorId { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
    }
}
