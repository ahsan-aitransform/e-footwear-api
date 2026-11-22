using EFootwear.BLL.Security.Abstractions;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Security.Repositories;

/// <summary>DAL for the permission rows of a user (sec.user_permissions). Only the difference is written; deleting needs the DELETE grant of migration 0100.</summary>
public sealed class UserPermissionRepository(DatabaseSession databaseSession) : IUserPermissionRepository
{
    public async Task<IReadOnlySet<string>> GetGrantedCodesAsync(Guid companyId, Guid userId, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var codes = await databaseContext.UserPermissionGrants.AsNoTracking()
            .Where(grant => grant.CompanyId == companyId && grant.UserId == userId)
            .Select(grant => grant.PermissionCode)
            .ToListAsync(cancellationToken);
        return codes.ToHashSet(StringComparer.Ordinal);
    }

    public async Task ApplyChangeAsync(Guid companyId, Guid userId, PermissionChange change, Guid grantedByUserId,
                                       CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);

        if (change.RemovedCodes.Count > 0)
        {
            var removedCodes = change.RemovedCodes.ToList();
            await databaseContext.UserPermissionGrants
                .Where(grant => grant.CompanyId == companyId && grant.UserId == userId && removedCodes.Contains(grant.PermissionCode))
                .ExecuteDeleteAsync(cancellationToken);
        }

        databaseContext.UserPermissionGrants.AddRange(change.AddedCodes.Select(permissionCode => new UserPermissionGrant
        {
            CompanyId = companyId,
            UserId = userId,
            PermissionCode = permissionCode,
            GrantedBy = grantedByUserId,
        }));
    }
}
