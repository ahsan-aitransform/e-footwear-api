using EFootwear.BLL.Abstractions;
using EFootwear.BLL.MasterData.Abstractions;
using EFootwear.BLL.MasterData.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.MasterData;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.MasterData.Repositories;

/// <summary>Data access for the sizes of a size group (master.sizes).</summary>
public sealed class SizeRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : ISizeRepository
{
    public async Task<IReadOnlyList<SizeResponse>> ListBySizeGroupAsync(Guid sizeGroupId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.SizeCodes.AsNoTracking()
            .Where(size => size.CompanyId == companyId && size.SizeGroupId == sizeGroupId)
            .OrderBy(size => size.SortOrder).ThenBy(size => size.Code)
            .Select(size => new SizeResponse(size.Id, size.SizeGroupId, size.Code, size.SortOrder))
            .ToListAsync(cancellationToken);
    }

    public async Task<SizeResponse?> FindAsync(Guid sizeGroupId, Guid sizeId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.SizeCodes.AsNoTracking()
            .Where(size => size.CompanyId == companyId && size.SizeGroupId == sizeGroupId && size.Id == sizeId)
            .Select(size => new SizeResponse(size.Id, size.SizeGroupId, size.Code, size.SortOrder))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> CodeExistsAsync(Guid sizeGroupId, string sizeCode, Guid? exceptSizeId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var lowerCode = sizeCode.ToLowerInvariant();
        return await databaseContext.SizeCodes.AsNoTracking().AnyAsync(size =>
            size.CompanyId == companyId && size.SizeGroupId == sizeGroupId && size.Code.ToLower() == lowerCode
            && (exceptSizeId == null || size.Id != exceptSizeId), cancellationToken);
    }

    public async Task<Guid> AddAsync(Guid sizeGroupId, string sizeCode, int sortOrder, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var size = new SizeCode { CompanyId = currentUser.CompanyId, SizeGroupId = sizeGroupId, Code = sizeCode, SortOrder = sortOrder };
        databaseContext.SizeCodes.Add(size);
        await databaseContext.SaveChangesAsync(cancellationToken);
        return size.Id;
    }

    public async Task UpdateAsync(Guid sizeGroupId, Guid sizeId, string sizeCode, int sortOrder, CancellationToken cancellationToken)
    {
        var size = await RequireTrackedSizeAsync(sizeGroupId, sizeId, cancellationToken);
        size.Code = sizeCode;
        size.SortOrder = sortOrder;
    }

    public async Task DeleteAsync(Guid sizeGroupId, Guid sizeId, CancellationToken cancellationToken)
    {
        var size = await RequireTrackedSizeAsync(sizeGroupId, sizeId, cancellationToken);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        databaseContext.SizeCodes.Remove(size);
    }

    private async Task<SizeCode> RequireTrackedSizeAsync(Guid sizeGroupId, Guid sizeId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.SizeCodes.SingleOrDefaultAsync(size => size.CompanyId == companyId && size.SizeGroupId == sizeGroupId && size.Id == sizeId, cancellationToken)
               ?? throw new EFootwear.Domain.Common.DomainException(EFootwear.Domain.Common.ErrorKind.NotFound, "The size was not found.");
    }
}
