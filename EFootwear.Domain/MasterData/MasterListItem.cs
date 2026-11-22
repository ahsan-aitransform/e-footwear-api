using EFootwear.Domain.Common;

namespace EFootwear.Domain.MasterData;

/// <summary>
/// One row of a company's master list (department, brand, season, gender, colour, size group, product type, unit, location,
/// city). The ten generated master tables of the schema (master.departments, master.brands ...) have exactly the same shape,
/// so one class serves all of them; <see cref="MasterListDefinition"/> says which table a list lives in.
/// Rows are never hard-deleted: IsDeleted, DeletedAt and DeletedBy keep the history (docx Common Rules, soft delete).
/// </summary>
public sealed class MasterListItem : IHasRowVersion
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = "";

    /// <summary>The name in Urdu (right-to-left text), optional.</summary>
    public string? NameUrdu { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public Guid? CreatedBy { get; set; }
    public long RowVersion { get; set; }
}
