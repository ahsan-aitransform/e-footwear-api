using EFootwear.BLL.Users;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Users;

/// <summary>
/// DAL for user accounts (EF Core). Each method selects the company first, so the request can be a platform
/// administrator's. Row-level security still applies, so a write to another company is refused by PostgreSQL.
/// </summary>
public sealed class UserRepository(DbSession db) : IUserRepository
{
    // Emails are unique across all companies, but row-level security hides other companies' users,
    // so the check goes through a database function that runs with owner rights and returns only yes or no.
    public async Task<bool> EmailExistsAsync(string email, CancellationToken ct)
    {
        var ctx = await db.DbAsync(ct);
        return await ctx.Database.SqlQuery<bool>($"select platform.email_in_use({email}) as \"Value\"").SingleAsync(ct);
    }

    public async Task<Guid> InsertAsync(Guid companyId, NewUser user, Guid? createdBy, CancellationToken ct)
    {
        db.UseCompany(companyId);
        var ctx = await db.DbAsync(ct);
        var entity = new AppUser
        {
            CompanyId = companyId,
            Username = user.Email,          // the email is also the sign-in name
            Email = user.Email,
            PasswordHash = user.PasswordHash,
            FullName = user.FullName,
            RoleCode = user.RoleCode,
            AllStores = user.AllStores,
            MustChangePassword = true,
            CreatedBy = createdBy,
        };
        ctx.Users.Add(entity);
        await ctx.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task AddStoresAsync(Guid companyId, Guid userId, IReadOnlyList<Guid> storeIds, CancellationToken ct)
    {
        db.UseCompany(companyId);
        var ctx = await db.DbAsync(ct);
        ctx.UserStores.AddRange(storeIds.Select(s => new UserStoreAccess { CompanyId = companyId, UserId = userId, StoreId = s }));
        await ctx.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<UserListItem>> ListAsync(Guid companyId, IReadOnlyList<Guid>? storeScope, CancellationToken ct)
    {
        db.UseCompany(companyId);
        var ctx = await db.DbAsync(ct);

        // Which stores each user works in (with the store code), for the whole company.
        var access = await (from us in ctx.UserStores.AsNoTracking()
                            join s in ctx.Stores.AsNoTracking()
                                on new { us.CompanyId, Id = us.StoreId } equals new { s.CompanyId, s.Id }
                            where us.CompanyId == companyId
                            orderby s.Id
                            select new { us.UserId, s.Id, s.Code }).ToListAsync(ct);
        var byUser = access.ToLookup(a => a.UserId);

        var users = await ctx.Users.AsNoTracking()
            .Where(u => u.CompanyId == companyId && !u.IsDeleted)
            .OrderBy(u => u.Id)
            .ToListAsync(ct);

        return users
            .Where(u => storeScope is null || byUser[u.Id].Any(a => storeScope.Contains(a.Id)))
            .Select(u => new UserListItem(u.Id, u.Email, u.FullName, u.RoleCode, u.AllStores, u.Status,
                                          u.MustChangePassword,
                                          byUser[u.Id].Select(a => a.Id).ToList(),
                                          byUser[u.Id].Select(a => a.Code).ToList()))
            .ToList();
    }
}
