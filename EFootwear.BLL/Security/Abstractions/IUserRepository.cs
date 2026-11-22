using EFootwear.BLL.Security.Contracts;

namespace EFootwear.BLL.Security.Abstractions;

/// <summary>BLL contract for the data layer: user accounts. Implemented in EFootwear.DAL/Security/Repositories/UserRepository.cs.</summary>
public interface IUserRepository
{
    /// <summary>True when any user of any company has this email (emails are unique platform-wide).</summary>
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    Task<Guid> InsertAsync(Guid companyId, NewUserRecord newUser, Guid? createdByUserId, CancellationToken cancellationToken);

    Task AddStoresAsync(Guid companyId, Guid userId, IReadOnlyList<Guid> storeIds, CancellationToken cancellationToken);

    /// <summary>Copies permission codes into sec.user_permissions for the user (used for the role template on creation).</summary>
    Task GrantPermissionsAsync(Guid companyId, Guid userId, IReadOnlyCollection<string> permissionCodes,
                               Guid? grantedByUserId, CancellationToken cancellationToken);

    /// <summary>Users of the company. With <paramref name="storeScope"/> set, only users who work in one of those stores.</summary>
    Task<IReadOnlyList<UserListItemResponse>> ListAsync(Guid companyId, IReadOnlyList<Guid>? storeScope,
                                                        CancellationToken cancellationToken);

    /// <summary>One user with stores and row version, or null when there is no such (non-deleted) user in the company.</summary>
    Task<UserDetailsResponse?> FindDetailsAsync(Guid companyId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Changes name, email and role. Throws a conflict when <paramref name="expectedRowVersion"/> is stale.</summary>
    Task UpdateProfileAsync(Guid companyId, Guid userId, UserProfileUpdateRecord update, long expectedRowVersion,
                            CancellationToken cancellationToken);

    /// <summary>Makes the user's store assignments exactly <paramref name="storeIds"/> (adds and removes only the difference).</summary>
    Task ReplaceStoresAsync(Guid companyId, Guid userId, IReadOnlyCollection<Guid> storeIds, CancellationToken cancellationToken);

    /// <summary>Blocks or unblocks through sec.set_user_status (ends the sessions of a blocked user, writes the login history).</summary>
    Task SetStatusAsync(Guid companyId, Guid userId, string status, CancellationToken cancellationToken);

    /// <summary>Sets a temporary password through sec.admin_reset_password (ends all sessions, forces a change at next sign-in).</summary>
    Task AdminResetPasswordAsync(Guid companyId, Guid userId, string temporaryPasswordHash, CancellationToken cancellationToken);

    /// <summary>Soft delete: the account is hidden and cannot sign in, its sessions end, its history stays.</summary>
    Task SoftDeleteAsync(Guid companyId, Guid userId, Guid deletedByUserId, CancellationToken cancellationToken);
}
