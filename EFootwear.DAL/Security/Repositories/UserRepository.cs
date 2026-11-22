using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PostgresErrors = Npgsql.PostgresErrorCodes;

namespace EFootwear.DAL.Security.Repositories;

/// <summary>
/// DAL for user accounts (EF Core). Each method selects the company first, so the request can be a platform
/// administrator's. Row-level security still applies, so a write to another company is refused by PostgreSQL.
/// Blocking and the administrator password reset call the database functions, which also end sessions and write the login history.
/// </summary>
public sealed class UserRepository(DatabaseSession databaseSession, IDateTimeProvider dateTimeProvider) : IUserRepository
{
    private const string UserEndedSessionsReason = "USER_DELETED";

    // Emails are unique across all companies, but row-level security hides other companies' users,
    // so the check goes through a database function that runs with owner rights and returns only yes or no.
    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        return await databaseContext.Database
            .SqlQuery<bool>($"select platform.email_in_use({email}) as \"Value\"")
            .SingleAsync(cancellationToken);
    }

    public async Task<Guid> InsertAsync(Guid companyId, NewUserRecord newUser, Guid? createdByUserId,
                                        CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var user = new CompanyUser
        {
            CompanyId = companyId,
            Username = newUser.Email,          // the email is also the sign-in name
            Email = newUser.Email,
            PasswordHash = newUser.PasswordHash,
            FullName = newUser.FullName,
            RoleCode = newUser.RoleCode,
            AllStores = newUser.AllStores,
            MustChangePassword = true,
            CreatedBy = createdByUserId,
        };
        databaseContext.CompanyUsers.Add(user);
        await databaseContext.SaveChangesAsync(cancellationToken);
        return user.Id;
    }

    public async Task AddStoresAsync(Guid companyId, Guid userId, IReadOnlyList<Guid> storeIds,
                                     CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        databaseContext.UserStoreAccesses.AddRange(storeIds.Select(storeId =>
            new UserStoreAccess { CompanyId = companyId, UserId = userId, StoreId = storeId }));
        await databaseContext.SaveChangesAsync(cancellationToken);
    }

    public async Task GrantPermissionsAsync(Guid companyId, Guid userId, IReadOnlyCollection<string> permissionCodes,
                                            Guid? grantedByUserId, CancellationToken cancellationToken)
    {
        if (permissionCodes.Count == 0) return;
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        databaseContext.UserPermissionGrants.AddRange(permissionCodes.Select(permissionCode => new UserPermissionGrant
        {
            CompanyId = companyId,
            UserId = userId,
            PermissionCode = permissionCode,
            GrantedBy = grantedByUserId,
        }));
        await databaseContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserListItemResponse>> ListAsync(Guid companyId, IReadOnlyList<Guid>? storeScope,
                                                                     CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var now = dateTimeProvider.UtcNow;

        var storeAccessesByUser = (await LoadStoreAccessesAsync(databaseContext, companyId, cancellationToken))
            .ToLookup(storeAccess => storeAccess.UserId);

        var users = await databaseContext.CompanyUsers.AsNoTracking()
            .Where(user => user.CompanyId == companyId && !user.IsDeleted)
            .OrderBy(user => user.Id)
            .ToListAsync(cancellationToken);

        var signedInUserIds = (await databaseContext.UserSessions.AsNoTracking()
            .Where(session => session.CompanyId == companyId && session.RevokedAt == null
                              && session.ExpiresAt > now && session.IdleExpiresAt > now)
            .Select(session => session.UserId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();

        return users
            .Where(user => storeScope is null || storeAccessesByUser[user.Id].Any(access => storeScope.Contains(access.StoreId)))
            .Select(user => new UserListItemResponse(user.Id, user.Email, user.FullName, user.RoleCode, user.AllStores,
                                                     user.Status, user.MustChangePassword,
                                                     storeAccessesByUser[user.Id].Select(access => access.StoreId).ToList(),
                                                     storeAccessesByUser[user.Id].Select(access => access.StoreCode).ToList(),
                                                     user.LastLoginAt, signedInUserIds.Contains(user.Id),
                                                     user.LockedUntil is { } lockedUntil && lockedUntil > now ? lockedUntil : null))
            .ToList();
    }

    public async Task<UserDetailsResponse?> FindDetailsAsync(Guid companyId, Guid userId, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var now = dateTimeProvider.UtcNow;

        var user = await databaseContext.CompanyUsers.AsNoTracking()
            .Where(candidate => candidate.CompanyId == companyId && candidate.Id == userId && !candidate.IsDeleted)
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null) return null;

        var storeAccesses = (await LoadStoreAccessesAsync(databaseContext, companyId, cancellationToken))
            .Where(access => access.UserId == userId)
            .ToList();
        return new UserDetailsResponse(user.Id, user.Email, user.FullName, user.RoleCode, user.AllStores, user.Status,
                                       user.MustChangePassword,
                                       user.LockedUntil is { } lockedUntil && lockedUntil > now ? lockedUntil : null,
                                       user.LastLoginAt, user.CreatedAt,
                                       storeAccesses.Select(access => access.StoreId).ToList(),
                                       storeAccesses.Select(access => access.StoreCode).ToList(), user.RowVersion);
    }

    public async Task UpdateProfileAsync(Guid companyId, Guid userId, UserProfileUpdateRecord update, long expectedRowVersion,
                                         CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var user = await databaseContext.CompanyUsers
            .SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == userId && !candidate.IsDeleted,
                                  cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "User not found.");

        // The save at commit is refused when the row changed since the screen was opened (409 CONCURRENT_UPDATE).
        databaseContext.Entry(user).Property(candidate => candidate.RowVersion).OriginalValue = expectedRowVersion;
        user.Email = update.Email;
        user.Username = update.Email;       // the email is also the sign-in name
        user.FullName = update.FullName;
        user.RoleCode = update.RoleCode;
        user.AllStores = update.HasAccessToAllStores;
    }

    public async Task ReplaceStoresAsync(Guid companyId, Guid userId, IReadOnlyCollection<Guid> storeIds,
                                         CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var currentAccesses = await databaseContext.UserStoreAccesses
            .Where(access => access.CompanyId == companyId && access.UserId == userId)
            .ToListAsync(cancellationToken);

        databaseContext.UserStoreAccesses.RemoveRange(currentAccesses.Where(access => !storeIds.Contains(access.StoreId)));
        var alreadyAssigned = currentAccesses.Select(access => access.StoreId).ToHashSet();
        databaseContext.UserStoreAccesses.AddRange(storeIds.Where(storeId => !alreadyAssigned.Contains(storeId))
            .Select(storeId => new UserStoreAccess { CompanyId = companyId, UserId = userId, StoreId = storeId }));
    }

    public async Task SetStatusAsync(Guid companyId, Guid userId, string status, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        try
        {
            await databaseContext.Database.ExecuteSqlAsync($"select sec.set_user_status({companyId}, {userId}, {status})", cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrors.NoDataFound)
        {
            throw new DomainException(ErrorKind.NotFound, "User not found.");
        }
    }

    public async Task AdminResetPasswordAsync(Guid companyId, Guid userId, string temporaryPasswordHash,
                                              CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        try
        {
            await databaseContext.Database.ExecuteSqlAsync(
                $"select sec.admin_reset_password({companyId}, {userId}, {temporaryPasswordHash})", cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrors.NoDataFound)
        {
            throw new DomainException(ErrorKind.NotFound, "User not found.");
        }
    }

    public async Task SoftDeleteAsync(Guid companyId, Guid userId, Guid deletedByUserId, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var deletedRows = await databaseContext.Database.ExecuteSqlAsync($"""
            update sec.users
               set is_deleted = true, deleted_at = now(), deleted_by = {deletedByUserId}, status = {UserStatusCodes.Disabled}
             where company_id = {companyId} and id = {userId} and not is_deleted
            """, cancellationToken);
        if (deletedRows == 0) throw new DomainException(ErrorKind.NotFound, "User not found.");

        await databaseContext.Database.ExecuteSqlAsync($"""
            update sec.sessions
               set revoked_at = now(), revoke_reason = {UserEndedSessionsReason}
             where company_id = {companyId} and user_id = {userId} and revoked_at is null
            """, cancellationToken);
    }

    private static async Task<List<StoreAccessRow>> LoadStoreAccessesAsync(ApplicationDatabaseContext databaseContext, Guid companyId,
                                                                           CancellationToken cancellationToken) =>
        await (from access in databaseContext.UserStoreAccesses.AsNoTracking()
               join store in databaseContext.Stores.AsNoTracking()
                   on new { access.CompanyId, Id = access.StoreId } equals new { store.CompanyId, store.Id }
               where access.CompanyId == companyId
               orderby store.Id
               select new StoreAccessRow(access.UserId, store.Id, store.Code))
              .ToListAsync(cancellationToken);

    private sealed record StoreAccessRow(Guid UserId, Guid StoreId, string StoreCode);
}
