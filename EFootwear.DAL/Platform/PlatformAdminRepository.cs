using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PgErrors = Npgsql.PostgresErrorCodes;

namespace EFootwear.DAL.Platform;

/// <summary>
/// DAL for platform administrator accounts (EF Core). These accounts have no company, so this repository uses its
/// own short-lived context and transaction instead of the company-scoped DbSession.
/// Each method commits its own work (a failed sign-in is recorded too, so lockout works).
/// </summary>
public sealed class PlatformAdminRepository(IDbContextFactory<AppDbContext> factory) : IPlatformAdminRepository
{
    public async Task<string?> GetPasswordHashAsync(string username, CancellationToken ct)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        return await ctx.Database
            .SqlQuery<string>($"select password_hash as \"Value\" from platform.admins where lower(username) = lower({username})")
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PlatformLoginOutcome> LoginAsync(string username, bool passwordOk, string? ip, string? userAgent, CancellationToken ct)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        var row = await ctx.Database.SqlQuery<LoginRow>($"""
            select out_outcome as "Outcome", out_admin_id as "AdminId", out_session_token as "SessionToken",
                   out_locked_until as "LockedUntil"
              from platform.admin_login({username}, {passwordOk}, {ip}::inet, {userAgent})
            """).SingleAsync(ct);
        await tx.CommitAsync(ct);
        return new PlatformLoginOutcome(row.Outcome, row.AdminId, row.SessionToken, row.LockedUntil);
    }

    public async Task<PlatformAdminIdentity> ValidateSessionAsync(string token, CancellationToken ct)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        SessionRow row;
        try
        {
            row = await ctx.Database.SqlQuery<SessionRow>($"""
                select out_admin_id as "AdminId", out_username as "Username", out_full_name as "FullName"
                  from platform.admin_session({token})
                """).SingleAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PgErrors.InvalidAuthorizationSpecification)
        {
            throw new DomainException(ErrorKind.Unauthorized, "Session is missing, expired or revoked.");
        }
        await tx.CommitAsync(ct);   // saves the slid idle timeout
        return new PlatformAdminIdentity(row.AdminId, row.Username, row.FullName);
    }

    public async Task LogoutAsync(string token, CancellationToken ct)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        await ctx.Database.ExecuteSqlAsync($"select platform.admin_logout({token})", ct);
        await tx.CommitAsync(ct);
    }

    private sealed class LoginRow
    {
        public string Outcome { get; set; } = "";
        public long? AdminId { get; set; }
        public string? SessionToken { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
    }

    private sealed class SessionRow
    {
        public long AdminId { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
    }
}
