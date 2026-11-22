using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.MasterData.Abstractions;
using EFootwear.BLL.MasterData.Contracts;
using EFootwear.DAL.Common;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.MasterData;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.MasterData.Repositories;

/// <summary>
/// One repository for the ten simple master lists: <see cref="MasterListDefinition.EntityName"/> picks the table. Every query is
/// limited to the caller's company (row-level security repeats the rule in the database) and hides soft-deleted rows.
/// </summary>
public sealed class MasterListRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IMasterListRepository
{
    private static readonly SortableFields<MasterListItem> SortableItemFields = new SortableFields<MasterListItem>()
        .Allow("name", item => item.Name)
        .Allow("code", item => item.Code)
        .Allow("sortOrder", item => item.SortOrder);

    public async Task<PagedResult<MasterListItemResponse>> ListAsync(MasterListDefinition definition, PageRequest pageRequest, bool includeInactive,
                                                                     CancellationToken cancellationToken)
    {
        var items = (await LiveItemsAsync(definition, cancellationToken)).AsNoTracking();
        if (!includeInactive) items = items.Where(item => item.IsActive);
        if (pageRequest.SearchText is { } searchText)
        {
            var pattern = LikePattern.Contains(searchText);
            items = items.Where(item => EF.Functions.ILike(item.Name, pattern, LikePattern.EscapeCharacter)
                                        || (item.NameUrdu != null && EF.Functions.ILike(item.NameUrdu, pattern, LikePattern.EscapeCharacter))
                                        || (item.Code != null && EF.Functions.ILike(item.Code, pattern, LikePattern.EscapeCharacter)));
        }

        var totalItemCount = await items.LongCountAsync(cancellationToken);
        var page = await items.ApplySortingAndPaging(pageRequest, SortableItemFields, item => item.Id)
            .Select(item => new MasterListItemResponse(item.Id, item.Code, item.Name, item.NameUrdu, item.SortOrder, item.IsActive, item.RowVersion))
            .ToListAsync(cancellationToken);
        return new PagedResult<MasterListItemResponse>(page, pageRequest.PageNumber, pageRequest.PageSize, totalItemCount);
    }

    public async Task<IReadOnlyList<MasterListOptionResponse>> ListActiveOptionsAsync(MasterListDefinition definition, CancellationToken cancellationToken) =>
        await (await LiveItemsAsync(definition, cancellationToken)).AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Name)
            .Select(item => new MasterListOptionResponse(item.Id, item.Code, item.Name, item.NameUrdu))
            .ToListAsync(cancellationToken);

    public async Task<MasterListItemResponse?> FindAsync(MasterListDefinition definition, Guid itemId, CancellationToken cancellationToken) =>
        await (await LiveItemsAsync(definition, cancellationToken)).AsNoTracking()
            .Where(item => item.Id == itemId)
            .Select(item => new MasterListItemResponse(item.Id, item.Code, item.Name, item.NameUrdu, item.SortOrder, item.IsActive, item.RowVersion))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> NameExistsAsync(MasterListDefinition definition, string name, Guid? exceptItemId, CancellationToken cancellationToken)
    {
        var lowerName = name.ToLowerInvariant();
        return await (await LiveItemsAsync(definition, cancellationToken)).AsNoTracking()
            .AnyAsync(item => item.Name.ToLower() == lowerName && (exceptItemId == null || item.Id != exceptItemId), cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListNamesAsync(MasterListDefinition definition, CancellationToken cancellationToken) =>
        await (await LiveItemsAsync(definition, cancellationToken)).AsNoTracking().Select(item => item.Name).ToListAsync(cancellationToken);

    public async Task<Guid> AddAsync(MasterListDefinition definition, CreateMasterListItemRequest request, Guid createdBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var item = new MasterListItem
        {
            CompanyId = currentUser.CompanyId,
            Code = request.Code,
            Name = request.Name,
            NameUrdu = request.NameUrdu,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
            CreatedBy = createdBy,
        };
        databaseContext.Set<MasterListItem>(definition.EntityName).Add(item);
        await databaseContext.SaveChangesAsync(cancellationToken);   // the database generates the id
        return item.Id;
    }

    public async Task UpdateAsync(MasterListDefinition definition, Guid itemId, UpdateMasterListItemRequest request, CancellationToken cancellationToken)
    {
        var item = await RequireTrackedItemAsync(definition, itemId, cancellationToken);
        item.Code = request.Code;
        item.Name = request.Name;
        item.NameUrdu = request.NameUrdu;
        item.SortOrder = request.SortOrder;
        item.IsActive = request.IsActive;

        // The screen saw this version; if somebody saved in between, the save fails with 409 instead of overwriting.
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        databaseContext.Entry(item).Property(entity => entity.RowVersion).OriginalValue = request.RowVersion;
    }

    public async Task SoftDeleteAsync(MasterListDefinition definition, Guid itemId, Guid deletedBy, CancellationToken cancellationToken)
    {
        var item = await RequireTrackedItemAsync(definition, itemId, cancellationToken);
        item.IsDeleted = true;
        item.DeletedAt = DateTimeOffset.UtcNow;
        item.DeletedBy = deletedBy;
    }

    private async Task<IQueryable<MasterListItem>> LiveItemsAsync(MasterListDefinition definition, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return databaseContext.Set<MasterListItem>(definition.EntityName).Where(item => item.CompanyId == companyId && !item.IsDeleted);
    }

    private async Task<MasterListItem> RequireTrackedItemAsync(MasterListDefinition definition, Guid itemId, CancellationToken cancellationToken) =>
        await (await LiveItemsAsync(definition, cancellationToken)).SingleOrDefaultAsync(item => item.Id == itemId, cancellationToken)
        ?? throw new EFootwear.Domain.Common.DomainException(EFootwear.Domain.Common.ErrorKind.NotFound, $"{definition.DisplayName} was not found.");
}
