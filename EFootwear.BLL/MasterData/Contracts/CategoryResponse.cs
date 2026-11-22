namespace EFootwear.BLL.MasterData.Contracts;

/// <summary>A category row (docx 2.5 grid: Id, Department, Category) with its parent when it is a sub-category.</summary>
public sealed record CategoryResponse(
    Guid Id,
    Guid DepartmentId,
    string DepartmentName,
    Guid? ParentCategoryId,
    string? ParentCategoryName,
    string? Code,
    string Name,
    string? NameUrdu,
    int SortOrder,
    bool IsActive,
    long RowVersion);
