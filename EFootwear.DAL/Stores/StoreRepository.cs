using EFootwear.BLL.Stores;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Stores;

// DAL (Data Access Layer, project EFootwear.DAL): the only place that talks to the database, through EF Core.
// It uses DbSession so every query runs inside the request's transaction, which already carries the caller's
// company and stores for row-level security.
public sealed class StoreRepository(DbSession db) : IStoreRepository
{
    // Row-level security limits this to the caller's company; core.store_allowed() limits it to assigned stores.
    public async Task<IReadOnlyList<StoreDto>> ListAccessibleAsync(CancellationToken ct)
    {
        var ctx = await db.DbAsync(ct);
        return await ctx.Stores
            .FromSql($"select * from core.stores where core.store_allowed(id)")
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .Select(s => new StoreDto(s.Id, s.Code, s.Name, s.IsHeadOffice, s.IsActive))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StoreDto>> ListForCompanyAsync(long companyId, CancellationToken ct)
    {
        db.UseCompany(companyId);
        var ctx = await db.DbAsync(ct);
        return await ctx.Stores.AsNoTracking()
            .Where(s => s.CompanyId == companyId)
            .OrderBy(s => s.Id)
            .Select(s => new StoreDto(s.Id, s.Code, s.Name, s.IsHeadOffice, s.IsActive))
            .ToListAsync(ct);
    }

    public async Task<bool> CodeExistsAsync(long companyId, string code, CancellationToken ct)
    {
        db.UseCompany(companyId);
        var ctx = await db.DbAsync(ct);
        return await ctx.Stores.AnyAsync(s => s.CompanyId == companyId && s.Code == code, ct);
    }

    public async Task<long> CreateAsync(long companyId, string code, string name, CancellationToken ct)
    {
        db.UseCompany(companyId);
        var ctx = await db.DbAsync(ct);
        var store = new Store { CompanyId = companyId, Code = code, Name = name };
        ctx.Stores.Add(store);
        await ctx.SaveChangesAsync(ct);   // the schema trigger then creates the store's accounts
        return store.Id;
    }

    public async Task<IReadOnlyList<long>> ActiveSellingStoreIdsAsync(long companyId, IReadOnlyCollection<long> storeIds, CancellationToken ct)
    {
        db.UseCompany(companyId);
        var ctx = await db.DbAsync(ct);
        var ids = storeIds.ToArray();
        return await ctx.Stores.AsNoTracking()
            .Where(s => s.CompanyId == companyId && ids.Contains(s.Id) && s.IsActive && !s.IsHeadOffice)
            .Select(s => s.Id)
            .ToListAsync(ct);
    }
}
