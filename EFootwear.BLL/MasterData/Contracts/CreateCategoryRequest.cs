namespace EFootwear.BLL.MasterData.Contracts;

/// <summary>Adds a category (docx 2.5): the department is required; the name is unique inside the department.</summary>
public sealed record CreateCategoryRequest(Guid DepartmentId, string Name, Guid? ParentCategoryId = null, string? NameUrdu = null,
                                           string? Code = null, int SortOrder = 0, bool IsActive = true);
