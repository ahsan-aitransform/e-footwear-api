namespace EFootwear.BLL.MasterData.Contracts;

/// <summary>Changes a category. RowVersion is the value the screen loaded; a stale value is refused with 409.</summary>
public sealed record UpdateCategoryRequest(Guid DepartmentId, string Name, Guid? ParentCategoryId, string? NameUrdu, string? Code,
                                           int SortOrder, bool IsActive, long RowVersion);
