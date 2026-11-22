using EFootwear.Domain.Security.Permissions;

namespace EFootwear.BLL.Security.Abstractions;

/// <summary>BLL contract for the permission rows of a user (sec.user_permissions).</summary>
public interface IUserPermissionRepository
{
    Task<IReadOnlySet<string>> GetGrantedCodesAsync(Guid companyId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Inserts the added codes and deletes the removed ones. Only the difference is written.</summary>
    Task ApplyChangeAsync(Guid companyId, Guid userId, PermissionChange change, Guid grantedByUserId,
                          CancellationToken cancellationToken);
}
