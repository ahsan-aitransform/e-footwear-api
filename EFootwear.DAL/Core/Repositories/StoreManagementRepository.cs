using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Core.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Core;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Core.Repositories;

/// <summary>Store administration with EF Core. A stale save throws DbUpdateConcurrencyException (the API answers 409).</summary>
public sealed class StoreManagementRepository(DatabaseSession databaseSession) : IStoreManagementRepository
{
    public async Task<IReadOnlyList<StoreDetailResponse>> ListAllAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var databaseContext = await GetDatabaseContextAsync(companyId, cancellationToken);
        return await databaseContext.Stores.AsNoTracking()
            .Where(store => store.CompanyId == companyId)
            .OrderByDescending(store => store.IsHeadOffice).ThenBy(store => store.Code)
            .Select(store => new StoreDetailResponse(store.Id, store.Code, store.Name, store.IsHeadOffice, store.IsActive,
                                                     store.Address, store.Phone, store.RowVersion))
            .ToListAsync(cancellationToken);
    }

    public async Task<StoreDetailResponse?> FindAsync(Guid companyId, Guid storeId, CancellationToken cancellationToken)
    {
        var databaseContext = await GetDatabaseContextAsync(companyId, cancellationToken);
        return await databaseContext.Stores.AsNoTracking()
            .Where(store => store.CompanyId == companyId && store.Id == storeId)
            .Select(store => new StoreDetailResponse(store.Id, store.Code, store.Name, store.IsHeadOffice, store.IsActive,
                                                     store.Address, store.Phone, store.RowVersion))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> CodeExistsAsync(Guid companyId, string code, CancellationToken cancellationToken)
    {
        var databaseContext = await GetDatabaseContextAsync(companyId, cancellationToken);
        var lowerCode = code.ToLower();
        return await databaseContext.Stores.AnyAsync(
            store => store.CompanyId == companyId && store.Code.ToLower() == lowerCode, cancellationToken);
    }

    public async Task<Guid> CreateAsync(Guid companyId, string code, string name, string? address, string? phone,
                                        CancellationToken cancellationToken)
    {
        var databaseContext = await GetDatabaseContextAsync(companyId, cancellationToken);
        var store = new Store { CompanyId = companyId, Code = code, Name = name, Address = address, Phone = phone };
        databaseContext.Stores.Add(store);
        await databaseContext.SaveChangesAsync(cancellationToken);   // the schema trigger then creates the store's accounts
        return store.Id;
    }

    public async Task UpdateAsync(Guid companyId, Guid storeId, string name, string? address, string? phone, long expectedRowVersion,
                                  CancellationToken cancellationToken)
    {
        var databaseContext = await GetDatabaseContextAsync(companyId, cancellationToken);
        var store = await databaseContext.Stores.SingleAsync(candidate => candidate.CompanyId == companyId && candidate.Id == storeId, cancellationToken);
        databaseContext.Entry(store).Property(candidate => candidate.RowVersion).OriginalValue = expectedRowVersion;
        store.Name = name;
        store.Address = address;
        store.Phone = phone;
        await databaseContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SetActiveAsync(Guid companyId, Guid storeId, bool isActive, CancellationToken cancellationToken)
    {
        var databaseContext = await GetDatabaseContextAsync(companyId, cancellationToken);
        var store = await databaseContext.Stores.SingleAsync(candidate => candidate.CompanyId == companyId && candidate.Id == storeId, cancellationToken);
        store.IsActive = isActive;
        await databaseContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ApplicationDatabaseContext> GetDatabaseContextAsync(Guid companyId, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        return await databaseSession.GetDatabaseContextAsync(cancellationToken);
    }
}
