using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Platform;
using EFootwear.BLL.Stores;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;

namespace EFootwear.BLL.Users;

public sealed record CreateUserRequest(long? CompanyId, string Email, string FullName, string RoleCode,
                                       IReadOnlyList<long>? StoreIds, string Password);

public sealed record CreationOptions(IReadOnlyList<string> Roles, IReadOnlyList<StoreDto> Stores, bool NeedsStore);

public sealed record NewUser(string Email, string PasswordHash, string FullName, string RoleCode, bool AllStores);

public sealed record UserListItem(long Id, string Email, string FullName, string RoleCode, bool AllStores,
                                  string Status, bool MustChangePassword, IReadOnlyList<long> StoreIds,
                                  IReadOnlyList<string> StoreCodes);

/// <summary>BLL contract for the DAL. Implemented in EFootwear.DAL/Users/UserRepository.cs.</summary>
public interface IUserRepository
{
    /// <summary>True when any user of any company has this email (emails are unique platform-wide).</summary>
    Task<bool> EmailExistsAsync(string email, CancellationToken ct);
    Task<long> InsertAsync(long companyId, NewUser user, long? createdBy, CancellationToken ct);
    Task AddStoresAsync(long companyId, long userId, IReadOnlyList<long> storeIds, CancellationToken ct);

    /// <summary>Users of the company. With storeScope set, only users who work in one of those stores.</summary>
    Task<IReadOnlyList<UserListItem>> ListAsync(long companyId, IReadOnlyList<long>? storeScope, CancellationToken ct);
}

/// <summary>
/// User management. Creation rules (who may create which role, in which stores) are in
/// UserCreationRules (Domain). This service decides the company, applies the rules and saves.
///
/// Company scope: a platform administrator names the company in the request. A company user is always
/// bound to their own company; naming another company is refused.
/// </summary>
public sealed class UserService(IUserRepository users, IStoreRepository stores, IPlatformRepository platform,
                                IPasswordHasher hasher, RequestContext context, IUnitOfWork uow)
{
    public async Task<CreationOptions> GetCreationOptionsAsync(long? companyId, CancellationToken ct)
    {
        var company = await ResolveCompanyAsync(companyId, ct);
        var roles = UserCreationRules.CreatableRoles(context.ToCaller());

        // Platform administrators see every store of the company. Company users see their accessible stores.
        var all = context.IsPlatformAdmin
            ? await stores.ListForCompanyAsync(company, ct)
            : await stores.ListAccessibleAsync(ct);
        var selling = all.Where(s => !s.IsHeadOffice && s.IsActive).ToList();

        return new CreationOptions(roles, selling, NeedsStore: roles.Any(r => r != RoleCodes.CompanyOwner));
    }

    public async Task<IReadOnlyList<UserListItem>> ListAsync(long? companyId, CancellationToken ct)
    {
        var company = await ResolveCompanyAsync(companyId, ct);

        // A store manager sees only the people who work in its stores. Owners and platform admins see all.
        IReadOnlyList<long>? scope = context.IsPlatformAdmin || context.AllStores
            ? null
            : context.StoreIds.ToList();
        return await users.ListAsync(company, scope, ct);
    }

    public async Task<long> CreateAsync(CreateUserRequest req, CancellationToken ct)
    {
        var company = await ResolveCompanyAsync(req.CompanyId, ct);

        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        var fullName = (req.FullName ?? "").Trim();
        UserCreationRules.EnsureValidAccount(email, fullName, req.Password ?? "");

        var storeIds = (req.StoreIds ?? []).Distinct().ToList();
        UserCreationRules.EnsureCanCreate(context.ToCaller(), req.RoleCode, storeIds);

        if (storeIds.Count > 0)
        {
            var valid = await stores.ActiveSellingStoreIdsAsync(company, storeIds, ct);
            if (valid.Count != storeIds.Count)
                throw new DomainException(ErrorKind.Validation, "One or more stores are not valid for this company.");
        }

        if (await users.EmailExistsAsync(email, ct))
            throw new DomainException(ErrorKind.Conflict, $"The email {email} is already in use.");

        var newUser = new NewUser(email, hasher.Hash(req.Password!), fullName, req.RoleCode,
                                  AllStores: req.RoleCode == RoleCodes.CompanyOwner);
        var createdBy = context.IsPlatformAdmin ? (long?)null : context.UserId;
        var id = await users.InsertAsync(company, newUser, createdBy, ct);
        if (storeIds.Count > 0)
            await users.AddStoresAsync(company, id, storeIds, ct);

        if (context.IsPlatformAdmin)
            await platform.LogAdminActionAsync(context.PlatformAdminId!.Value, "USER_CREATED", company,
                                               new { userId = id, email, role = req.RoleCode, storeIds }, ct);

        await uow.CommitAsync(ct);
        return id;
    }

    /// <summary>Platform administrators name the company. Company users are always their own company.</summary>
    private async Task<long> ResolveCompanyAsync(long? requested, CancellationToken ct)
    {
        if (context.IsPlatformAdmin)
        {
            if (requested is not > 0)
                throw new DomainException(ErrorKind.Validation, "Choose the company.");
            if (!await platform.CompanyExistsAsync(requested.Value, ct))
                throw new DomainException(ErrorKind.NotFound, "Company not found.");
            return requested.Value;
        }

        if (!context.IsAuthenticated)
            throw new DomainException(ErrorKind.Unauthorized, "Sign in first.");
        if (requested is { } r && r != context.CompanyId)
            throw new DomainException(ErrorKind.Forbidden, "You can only manage users of your own company.");
        return context.CompanyId;
    }
}
