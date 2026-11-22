using EFootwear.Domain.Common;

namespace EFootwear.Domain.MasterData;

/// <summary>
/// A product category. Table: master.categories. Every category belongs to a department (docx 2.5) and may sit under another
/// category of the same department. The name is unique inside its department, without regard to case.
/// </summary>
public sealed class Category : IHasRowVersion
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid? ParentCategoryId { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = "";
    public string? NameUrdu { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public Guid? CreatedBy { get; set; }
    public long RowVersion { get; set; }
}
