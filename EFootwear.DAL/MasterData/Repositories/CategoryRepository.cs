using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.MasterData.Abstractions;
using EFootwear.BLL.MasterData.Contracts;
using EFootwear.DAL.Common;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Common;
using EFootwear.Domain.MasterData;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.MasterData.Repositories;

/// <summary>Data access for categories (master.categories), joined with the department and parent names the screen shows.</summary>
public sealed class CategoryRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : ICategoryRepository
{
    private static readonly SortableFields<CategoryRow> SortableCategoryFields = new SortableFields<CategoryRow>()
        .Allow("name", row => row.Category.Name)
        .Allow("department", row => row.DepartmentName)
        .Allow("sortOrder", row => row.Category.SortOrder);

    public async Task<PagedResult<CategoryResponse>> ListAsync(Guid? departmentId, PageRequest pageRequest, bool includeInactive,
                                                               CancellationToken cancellationToken)
    {
        var rows = await RowsAsync(cancellationToken);
        if (departmentId is { } selectedDepartmentId) rows = rows.Where(row => row.Category.DepartmentId == selectedDepartmentId);
        if (!includeInactive) rows = rows.Where(row => row.Category.IsActive);
        if (pageRequest.SearchText is { } searchText)
        {
            var pattern = LikePattern.Contains(searchText);
            rows = rows.Where(row => EF.Functions.ILike(row.Category.Name, pattern, LikePattern.EscapeCharacter)
                                     || EF.Functions.ILike(row.DepartmentName, pattern, LikePattern.EscapeCharacter));
        }

        var totalItemCount = await rows.LongCountAsync(cancellationToken);
        var page = await rows.ApplySortingAndPaging(pageRequest, SortableCategoryFields, row => row.Category.Id).ToListAsync(cancellationToken);
        return new PagedResult<CategoryResponse>(page.Select(ToResponse).ToList(), pageRequest.PageNumber, pageRequest.PageSize, totalItemCount);
    }

    public async Task<IReadOnlyList<MasterListOptionResponse>> ListActiveOptionsAsync(Guid? departmentId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var categories = databaseContext.Categories.AsNoTracking().Where(category => category.CompanyId == companyId && !category.IsDeleted && category.IsActive);
        if (departmentId is { } selectedDepartmentId) categories = categories.Where(category => category.DepartmentId == selectedDepartmentId);
        return await categories.OrderBy(category => category.SortOrder).ThenBy(category => category.Name)
            .Select(category => new MasterListOptionResponse(category.Id, category.Code, category.Name, category.NameUrdu))
            .ToListAsync(cancellationToken);
    }

    public async Task<CategoryResponse?> FindAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        var row = await (await RowsAsync(cancellationToken)).Where(candidate => candidate.Category.Id == categoryId).SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : ToResponse(row);
    }

    public async Task<bool> NameExistsInDepartmentAsync(Guid departmentId, string name, Guid? exceptCategoryId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var lowerName = name.ToLowerInvariant();
        return await databaseContext.Categories.AsNoTracking().AnyAsync(category =>
            category.CompanyId == companyId && !category.IsDeleted && category.DepartmentId == departmentId
            && category.Name.ToLower() == lowerName && (exceptCategoryId == null || category.Id != exceptCategoryId), cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListAncestorIdsAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var ancestorIds = new List<Guid> { categoryId };
        var currentId = categoryId;
        // The tree is shallow; walking up one parent at a time stops at the root or when a loop in old data would repeat an id.
        while (true)
        {
            var parentId = await databaseContext.Categories.AsNoTracking()
                .Where(category => category.CompanyId == companyId && category.Id == currentId)
                .Select(category => category.ParentCategoryId).SingleOrDefaultAsync(cancellationToken);
            if (parentId is null || ancestorIds.Contains(parentId.Value)) return ancestorIds;
            ancestorIds.Add(parentId.Value);
            currentId = parentId.Value;
        }
    }

    public async Task<Guid> AddAsync(CreateCategoryRequest normalizedRequest, Guid createdBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var category = new Category
        {
            CompanyId = currentUser.CompanyId,
            DepartmentId = normalizedRequest.DepartmentId,
            ParentCategoryId = normalizedRequest.ParentCategoryId,
            Code = normalizedRequest.Code,
            Name = normalizedRequest.Name,
            NameUrdu = normalizedRequest.NameUrdu,
            SortOrder = normalizedRequest.SortOrder,
            IsActive = normalizedRequest.IsActive,
            CreatedBy = createdBy,
        };
        databaseContext.Categories.Add(category);
        await databaseContext.SaveChangesAsync(cancellationToken);
        return category.Id;
    }

    public async Task UpdateAsync(Guid categoryId, UpdateCategoryRequest normalizedRequest, CancellationToken cancellationToken)
    {
        var category = await RequireTrackedCategoryAsync(categoryId, cancellationToken);
        category.DepartmentId = normalizedRequest.DepartmentId;
        category.ParentCategoryId = normalizedRequest.ParentCategoryId;
        category.Code = normalizedRequest.Code;
        category.Name = normalizedRequest.Name;
        category.NameUrdu = normalizedRequest.NameUrdu;
        category.SortOrder = normalizedRequest.SortOrder;
        category.IsActive = normalizedRequest.IsActive;
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        databaseContext.Entry(category).Property(entity => entity.RowVersion).OriginalValue = normalizedRequest.RowVersion;
    }

    public async Task SoftDeleteAsync(Guid categoryId, Guid deletedBy, CancellationToken cancellationToken)
    {
        var category = await RequireTrackedCategoryAsync(categoryId, cancellationToken);
        category.IsDeleted = true;
        category.DeletedAt = DateTimeOffset.UtcNow;
        category.DeletedBy = deletedBy;
    }

    private async Task<IQueryable<CategoryRow>> RowsAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var departments = databaseContext.Set<MasterListItem>(MasterListDefinitions.Department.EntityName).AsNoTracking();
        return from category in databaseContext.Categories.AsNoTracking()
               where category.CompanyId == companyId && !category.IsDeleted
               join department in departments on new { category.CompanyId, Id = category.DepartmentId } equals new { department.CompanyId, department.Id }
               join parent in databaseContext.Categories.AsNoTracking() on new { category.CompanyId, Id = category.ParentCategoryId }
                   equals new { parent.CompanyId, Id = (Guid?)parent.Id } into parentGroup
               from parent in parentGroup.DefaultIfEmpty()
               select new CategoryRow { Category = category, DepartmentName = department.Name, ParentCategoryName = parent == null ? null : parent.Name };
    }

    private async Task<Category> RequireTrackedCategoryAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.Categories.SingleOrDefaultAsync(category => category.CompanyId == companyId && category.Id == categoryId && !category.IsDeleted, cancellationToken)
               ?? throw new DomainException(ErrorKind.NotFound, "The category was not found.");
    }

    private static CategoryResponse ToResponse(CategoryRow row) => new(
        row.Category.Id, row.Category.DepartmentId, row.DepartmentName, row.Category.ParentCategoryId, row.ParentCategoryName,
        row.Category.Code, row.Category.Name, row.Category.NameUrdu, row.Category.SortOrder, row.Category.IsActive, row.Category.RowVersion);

    private sealed class CategoryRow
    {
        public Category Category { get; set; } = null!;
        public string DepartmentName { get; set; } = "";
        public string? ParentCategoryName { get; set; }
    }
}
