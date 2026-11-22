using EFootwear.Domain.Common;

namespace EFootwear.Domain.Core;

/// <summary>
/// A store of one company. Table: core.stores. The head office is the store with the nil id
/// (00000000-0000-0000-0000-000000000000), so a store with IsHeadOffice = true always has that id.
/// </summary>
public sealed class Store : IHasRowVersion
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsHeadOffice { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public long RowVersion { get; set; }
}
