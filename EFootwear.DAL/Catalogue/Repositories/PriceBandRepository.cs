using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Catalogue;
using EFootwear.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EFootwear.DAL.Catalogue.Repositories;

/// <summary>
/// Price bands (catalog.price_bands). The database refuses overlapping ranges with an exclusion constraint (SQLSTATE 23P01); it is turned into a
/// clear conflict message here, so the changes are saved inside the repository methods that can cause it.
/// </summary>
public sealed class PriceBandRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IPriceBandRepository
{
    public async Task<IReadOnlyList<PriceBandResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.PriceBands.AsNoTracking().Where(band => band.CompanyId == companyId && !band.IsDeleted)
            .OrderBy(band => band.SortOrder).ThenBy(band => band.MinPrice)
            .Select(band => new PriceBandResponse(band.Id, band.Name, band.MinPrice, band.MaxPrice, band.SortOrder, band.RowVersion))
            .ToListAsync(cancellationToken);
    }

    public async Task<PriceBandResponse?> FindAsync(Guid priceBandId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.PriceBands.AsNoTracking().Where(band => band.CompanyId == companyId && band.Id == priceBandId && !band.IsDeleted)
            .Select(band => new PriceBandResponse(band.Id, band.Name, band.MinPrice, band.MaxPrice, band.SortOrder, band.RowVersion))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid> AddAsync(string name, long minimumPrice, long? maximumPrice, int sortOrder, Guid createdBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var band = new PriceBand
        {
            CompanyId = currentUser.CompanyId, Id = Guid.CreateVersion7(), Name = name, MinPrice = minimumPrice, MaxPrice = maximumPrice,
            SortOrder = sortOrder, CreatedBy = createdBy,
        };
        databaseContext.PriceBands.Add(band);
        await SaveTranslatingOverlapAsync(databaseContext, cancellationToken);
        return band.Id;
    }

    public async Task UpdateAsync(Guid priceBandId, string name, long minimumPrice, long? maximumPrice, int sortOrder, long rowVersion, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var band = await databaseContext.PriceBands.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == priceBandId && !candidate.IsDeleted, cancellationToken)
                   ?? throw new DomainException(ErrorKind.NotFound, "The price band was not found.");
        band.Name = name;
        band.MinPrice = minimumPrice;
        band.MaxPrice = maximumPrice;
        band.SortOrder = sortOrder;
        databaseContext.Entry(band).Property(candidate => candidate.RowVersion).OriginalValue = rowVersion;
        await SaveTranslatingOverlapAsync(databaseContext, cancellationToken);
    }

    public async Task SoftDeleteAsync(Guid priceBandId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var band = await databaseContext.PriceBands.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == priceBandId && !candidate.IsDeleted, cancellationToken)
                   ?? throw new DomainException(ErrorKind.NotFound, "The price band was not found.");
        band.IsDeleted = true;
    }

    private static async Task SaveTranslatingOverlapAsync(ApplicationDatabaseContext databaseContext, CancellationToken cancellationToken)
    {
        try
        {
            await databaseContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation })
        {
            throw new DomainException(ErrorKind.Conflict, "This price range overlaps another price band.", ErrorCodes.DatabaseRuleViolation);
        }
    }
}
