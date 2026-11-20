using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PgErrors = Npgsql.PostgresErrorCodes;

namespace EFootwear.DAL.Auth;

/// <summary>
/// DAL for company login (EF Core). Reads go through the entities; the sec.* functions are called with raw SQL
/// because all rules (lockout, expiry, history) live in the database.
/// </summary>
public sealed class AuthRepository(DbSession db, RequestContext context) : IAuthRepository
{
    public void UseCompany(long companyId) => db.UseCompany(companyId);

    public async Task<long?> FindCompanyIdByEmailAsync(string email, CancellationToken ct)
    {
        var ctx = await db.DbAsync(ct);
        return await ctx.Database
            .SqlQuery<long?>($"select platform.find_company_by_user_email({email}) as \"Value\"")
            .SingleAsync(ct);
    }

    public async Task<string?> GetPasswordHashAsync(string username, CancellationToken ct)
    {
        var ctx = await db.DbAsync(ct);
        var name = username.ToLower();
        return await ctx.Users.AsNoTracking()
            .Where(u => u.Username.ToLower() == name && !u.IsDeleted)
            .Select(u => u.PasswordHash)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<string?> GetPasswordHashByUserIdAsync(long companyId, long userId, CancellationToken ct)
    {
        db.UseCompany(companyId);
        var ctx = await db.DbAsync(ct);
        return await ctx.Users.AsNoTracking()
            .Where(u => u.CompanyId == companyId && u.Id == userId)
            .Select(u => u.PasswordHash)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<LoginOutcome> LoginAsync(long companyId, string username, bool passwordOk,
                                               string? ip, string? userAgent, CancellationToken ct)
    {
        var ctx = await db.DbAsync(ct);
        try
        {
            var row = await ctx.Database.SqlQuery<LoginRow>($"""
                select out_outcome as "Outcome", out_user_id as "UserId", out_session_token as "SessionToken",
                       out_must_change_password as "MustChangePassword", out_locked_until as "LockedUntil"
                  from sec.login({companyId}, {username}, {passwordOk}, {ip}::inet, null::text, {userAgent})
                """).SingleAsync(ct);
            return new LoginOutcome(row.Outcome, row.UserId, row.SessionToken,
                                    row.MustChangePassword ?? false, row.LockedUntil);
        }
        catch (PostgresException ex) when (ex.SqlState is PgErrors.CheckViolation or PgErrors.ForeignKeyViolation)
        {
            // unknown company or company without an auth policy: indistinguishable from a wrong password
            throw new DomainException(ErrorKind.Unauthorized, "Invalid email or password.");
        }
    }

    public async Task<SessionIdentity> ValidateSessionAsync(long companyId, string token, CancellationToken ct)
    {
        var ctx = await db.DbAsync(ct);
        long userId;
        try
        {
            userId = await ctx.Database
                .SqlQuery<long>($"select sec.validate_session({companyId}, {token}) as \"Value\"")
                .SingleAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PgErrors.InvalidAuthorizationSpecification)
        {
            throw new DomainException(ErrorKind.Unauthorized, "Session is missing, expired or revoked.");
        }

        var user = await ctx.Users.AsNoTracking()
            .Where(u => u.CompanyId == companyId && u.Id == userId)
            .Select(u => new { u.RoleCode, u.AllStores, u.MustChangePassword })
            .SingleAsync(ct);
        var stores = await ctx.UserStores.AsNoTracking()
            .Where(s => s.CompanyId == companyId && s.UserId == userId)
            .OrderBy(s => s.StoreId)
            .Select(s => s.StoreId)
            .ToListAsync(ct);

        return new SessionIdentity(userId, user.RoleCode, user.AllStores, stores, user.MustChangePassword);
    }

    public async Task LogoutAsync(long companyId, string token, CancellationToken ct)
    {
        var ctx = await db.DbAsync(ct);
        await ctx.Database.ExecuteSqlAsync($"select sec.logout({companyId}, {token})", ct);
    }

    public async Task ChangePasswordAsync(long companyId, long userId, string newPasswordHash, CancellationToken ct)
    {
        db.UseCompany(companyId);
        var ctx = await db.DbAsync(ct);
        await ctx.Database.ExecuteSqlAsync($"select sec.change_password({companyId}, {userId}, true, {newPasswordHash})", ct);
    }

    public async Task RequirePermissionAsync(string permissionCode, CancellationToken ct)
    {
        var ctx = await db.DbAsync(ct);
        try
        {
            await ctx.Database.ExecuteSqlAsync(
                $"select sec.require_permission({context.CompanyId}, {context.UserId}, {permissionCode})", ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PgErrors.InsufficientPrivilege)
        {
            throw new DomainException(ErrorKind.Forbidden, $"Missing permission {permissionCode}.");
        }
    }

    private sealed class LoginRow
    {
        public string Outcome { get; set; } = "";
        public long? UserId { get; set; }
        public string? SessionToken { get; set; }
        public bool? MustChangePassword { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
    }
}
