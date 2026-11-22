using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PostgresErrors = Npgsql.PostgresErrorCodes;

namespace EFootwear.DAL.Security.Repositories;

/// <summary>
/// DAL for company login (EF Core). Reads go through the entities; the sec.* functions are called with raw SQL
/// because all rules (lockout, expiry, history) live in the database.
/// </summary>
public sealed class UserAuthenticationRepository(DatabaseSession databaseSession) : IUserAuthenticationRepository
{
    public void UseCompany(Guid companyId) => databaseSession.UseCompany(companyId);

    public async Task<Guid?> FindCompanyIdByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        return await databaseContext.Database
            .SqlQuery<Guid?>($"select platform.find_company_by_user_email({email}) as \"Value\"")
            .SingleAsync(cancellationToken);
    }

    public async Task<string?> GetPasswordHashAsync(string username, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var lowerCaseUsername = username.ToLower();
        return await databaseContext.CompanyUsers.AsNoTracking()
            .Where(user => user.Username.ToLower() == lowerCaseUsername && !user.IsDeleted)
            .Select(user => user.PasswordHash)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> GetPasswordHashByUserIdAsync(Guid companyId, Guid userId, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        return await databaseContext.CompanyUsers.AsNoTracking()
            .Where(user => user.CompanyId == companyId && user.Id == userId)
            .Select(user => user.PasswordHash)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<LoginOutcome> LoginAsync(Guid companyId, string username, bool isPasswordCorrect,
                                               string? clientIpAddress, string? userAgent,
                                               CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        try
        {
            var row = await databaseContext.Database.SqlQuery<LoginRow>($"""
                select out_outcome as "Outcome", out_user_id as "UserId", out_session_token as "SessionToken",
                       out_must_change_password as "MustChangePassword", out_locked_until as "LockedUntil"
                  from sec.login({companyId}, {username}, {isPasswordCorrect}, {clientIpAddress}::inet, null::text, {userAgent})
                """).SingleAsync(cancellationToken);
            return new LoginOutcome(row.Outcome, row.UserId, row.SessionToken,
                                    row.MustChangePassword ?? false, row.LockedUntil);
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrors.CheckViolation or PostgresErrors.ForeignKeyViolation)
        {
            // unknown company or company without an auth policy: indistinguishable from a wrong password
            throw new DomainException(ErrorKind.Unauthorized, "Invalid email or password.");
        }
    }

    public async Task<SessionIdentity> ValidateSessionAsync(Guid companyId, string token, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        Guid userId;
        try
        {
            userId = await databaseContext.Database
                .SqlQuery<Guid>($"select sec.validate_session({companyId}, {token}) as \"Value\"")
                .SingleAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrors.InvalidAuthorizationSpecification)
        {
            throw new DomainException(ErrorKind.Unauthorized, "Session is missing, expired or revoked.");
        }

        var user = await databaseContext.CompanyUsers.AsNoTracking()
            .Where(candidate => candidate.CompanyId == companyId && candidate.Id == userId)
            .Select(candidate => new { candidate.RoleCode, candidate.AllStores, candidate.MustChangePassword })
            .SingleAsync(cancellationToken);
        var assignedStoreIds = await databaseContext.UserStoreAccesses.AsNoTracking()
            .Where(access => access.CompanyId == companyId && access.UserId == userId)
            .OrderBy(access => access.StoreId)
            .Select(access => access.StoreId)
            .ToListAsync(cancellationToken);
        var grantedPermissionCodes = await databaseContext.UserPermissionGrants.AsNoTracking()
            .Where(grant => grant.CompanyId == companyId && grant.UserId == userId)
            .Select(grant => grant.PermissionCode)
            .ToListAsync(cancellationToken);

        return new SessionIdentity(userId, user.RoleCode, user.AllStores, assignedStoreIds, user.MustChangePassword,
                                   grantedPermissionCodes.ToHashSet(StringComparer.Ordinal));
    }

    public async Task LogoutAsync(Guid companyId, string token, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        await databaseContext.Database.ExecuteSqlAsync($"select sec.logout({companyId}, {token})", cancellationToken);
    }

    public async Task ChangePasswordAsync(Guid companyId, Guid userId, string newPasswordHash, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        await databaseContext.Database.ExecuteSqlAsync(
            $"select sec.change_password({companyId}, {userId}, true, {newPasswordHash})", cancellationToken);
    }

    public async Task<CurrentUserProfile> GetCurrentUserProfileAsync(Guid companyId, Guid userId, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var user = await databaseContext.CompanyUsers.AsNoTracking()
            .Where(candidate => candidate.CompanyId == companyId && candidate.Id == userId)
            .Select(candidate => new { candidate.FullName, candidate.Email })
            .SingleAsync(cancellationToken);
        var companyName = await databaseContext.Companies.AsNoTracking()
            .Where(company => company.Id == companyId)
            .Select(company => company.Name)
            .SingleAsync(cancellationToken);
        return new CurrentUserProfile(user.FullName, user.Email, companyName);
    }

    private sealed class LoginRow
    {
        public string Outcome { get; set; } = "";
        public Guid? UserId { get; set; }
        public string? SessionToken { get; set; }
        public bool? MustChangePassword { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
    }
}
