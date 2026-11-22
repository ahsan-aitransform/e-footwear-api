using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.MasterData.Abstractions;
using EFootwear.BLL.MasterData.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.MasterData;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.MasterData.Services;

/// <summary>
/// Use cases of the Category screen (docx 2.5). A category belongs to an active department; its name is unique inside the
/// department; a parent category must be of the same department and may never be the category itself or one of its own
/// descendants; a category that anything still uses (products, sub-categories) cannot be deleted.
/// </summary>
public sealed class CategoryService(
    ICategoryRepository categoryRepository,
    IMasterListRepository masterListRepository,
    IRowUsageRepository rowUsageRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<CategoryService> logger)
{
    private const string DisplayName = "Category";
    private const string CategoriesTableName = "master.categories";

    public Task<PagedResult<CategoryResponse>> ListAsync(Guid? departmentId, PageRequest pageRequest, bool includeInactive,
                                                         CancellationToken cancellationToken) =>
        categoryRepository.ListAsync(departmentId, pageRequest.Normalized(), includeInactive, cancellationToken);

    public Task<IReadOnlyList<MasterListOptionResponse>> ListOptionsAsync(Guid? departmentId, CancellationToken cancellationToken) =>
        categoryRepository.ListActiveOptionsAsync(departmentId, cancellationToken);

    public async Task<CategoryResponse> GetAsync(Guid categoryId, CancellationToken cancellationToken) =>
        await categoryRepository.FindAsync(categoryId, cancellationToken)
        ?? throw new DomainException(ErrorKind.NotFound, "The category was not found.");

    public async Task<CreatedIdResponse> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var normalized = request with
        {
            Name = MasterListRules.NormalizeText(request.Name),
            NameUrdu = MasterListRules.NormalizeOptionalText(request.NameUrdu),
            Code = MasterListRules.NormalizeOptionalText(request.Code),
        };
        MasterListRules.EnsureValid(normalized.Name, normalized.NameUrdu, normalized.Code, normalized.SortOrder, DisplayName);
        await EnsureDepartmentAndParentAsync(normalized.DepartmentId, normalized.ParentCategoryId, null, cancellationToken);
        await EnsureNameIsFreeInDepartmentAsync(normalized.DepartmentId, normalized.Name, null, cancellationToken);

        var categoryId = await categoryRepository.AddAsync(normalized, currentUser.UserId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Category {CategoryId} created in company {CompanyId} by user {UserId}", categoryId, currentUser.CompanyId, currentUser.UserId);
        return new CreatedIdResponse(categoryId);
    }

    public async Task UpdateAsync(Guid categoryId, UpdateCategoryRequest request, CancellationToken cancellationToken)
    {
        _ = await GetAsync(categoryId, cancellationToken);
        var normalized = request with
        {
            Name = MasterListRules.NormalizeText(request.Name),
            NameUrdu = MasterListRules.NormalizeOptionalText(request.NameUrdu),
            Code = MasterListRules.NormalizeOptionalText(request.Code),
        };
        MasterListRules.EnsureValid(normalized.Name, normalized.NameUrdu, normalized.Code, normalized.SortOrder, DisplayName);
        await EnsureDepartmentAndParentAsync(normalized.DepartmentId, normalized.ParentCategoryId, categoryId, cancellationToken);
        await EnsureNameIsFreeInDepartmentAsync(normalized.DepartmentId, normalized.Name, categoryId, cancellationToken);

        await categoryRepository.UpdateAsync(categoryId, normalized, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        var category = await GetAsync(categoryId, cancellationToken);
        var usages = await rowUsageRepository.FindUsagesAsync(CategoriesTableName, categoryId, cancellationToken);
        RowUsageMessage.ThrowWhenUsed($"Category \"{category.Name}\"", usages);

        await categoryRepository.SoftDeleteAsync(categoryId, currentUser.UserId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Category {CategoryId} deleted in company {CompanyId} by user {UserId}", categoryId, currentUser.CompanyId, currentUser.UserId);
    }

    private async Task EnsureDepartmentAndParentAsync(Guid departmentId, Guid? parentCategoryId, Guid? categoryId, CancellationToken cancellationToken)
    {
        var department = await masterListRepository.FindAsync(MasterListDefinitions.Department, departmentId, cancellationToken);
        if (department is null || !department.IsActive)
            throw new DomainException(ErrorKind.Validation, "Choose an active department.");
        if (parentCategoryId is null) return;

        var parent = await categoryRepository.FindAsync(parentCategoryId.Value, cancellationToken)
                     ?? throw new DomainException(ErrorKind.Validation, "The parent category was not found.");
        if (parent.DepartmentId != departmentId)
            throw new DomainException(ErrorKind.Validation, "The parent category must belong to the same department.");
        if (categoryId is null) return;

        if (parentCategoryId == categoryId)
            throw new DomainException(ErrorKind.Validation, "A category cannot be its own parent.");
        var ancestorIds = await categoryRepository.ListAncestorIdsAsync(parentCategoryId.Value, cancellationToken);
        if (ancestorIds.Contains(categoryId.Value))
            throw new DomainException(ErrorKind.Validation, "A category cannot be placed under one of its own sub-categories.");
    }

    private async Task EnsureNameIsFreeInDepartmentAsync(Guid departmentId, string name, Guid? exceptCategoryId, CancellationToken cancellationToken)
    {
        if (await categoryRepository.NameExistsInDepartmentAsync(departmentId, name, exceptCategoryId, cancellationToken))
            throw new DomainException(ErrorKind.Conflict, $"This department already has a category \"{name}\".");
    }
}
