using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Core.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Core;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Core.Repositories;

// DAL (Data Access Layer, project EFootwear.DAL): the only place that talks to the database, through EF Core.
// It uses DatabaseSession so every query runs inside the request's transaction, which already carries the caller's
// company and stores for row-level security.
public sealed class StoreRepository(DatabaseSession databaseSession) : IStoreRepository
{
    // Row-level security limits this to the caller's company; core.store_allowed() limits it to assigned stores.
    public async Task<IReadOnlyList<StoreResponse>> ListAccessibleAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        return await databaseContext.Stores
            .FromSql($"select * from core.stores where core.store_allowed(id)")
            .AsNoTracking()
            .OrderBy(store => store.Id)
            .Select(store => new StoreResponse(store.Id, store.Code, store.Name, store.IsHeadOffice, store.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoreResponse>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        return await databaseContext.Stores.AsNoTracking()
            .Where(store => store.CompanyId == companyId)
            .OrderBy(store => store.Id)
            .Select(store => new StoreResponse(store.Id, store.Code, store.Name, store.IsHeadOffice, store.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> CodeExistsAsync(Guid companyId, string code, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        return await databaseContext.Stores.AnyAsync(store => store.CompanyId == companyId && store.Code == code, cancellationToken);
    }

    public async Task<Guid> CreateAsync(Guid companyId, string code, string name, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var store = new Store { CompanyId = companyId, Code = code, Name = name };
        databaseContext.Stores.Add(store);
        await databaseContext.SaveChangesAsync(cancellationToken);   // the schema trigger then creates the store's accounts
        return store.Id;
    }

    public async Task<IReadOnlyList<Guid>> ActiveSellingStoreIdsAsync(Guid companyId, IReadOnlyCollection<Guid> storeIds,
                                                                      CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var requestedStoreIds = storeIds.ToArray();
        return await databaseContext.Stores.AsNoTracking()
            .Where(store => store.CompanyId == companyId && requestedStoreIds.Contains(store.Id) && store.IsActive && !store.IsHeadOffice)
            .Select(store => store.Id)
            .ToListAsync(cancellationToken);
    }
}
