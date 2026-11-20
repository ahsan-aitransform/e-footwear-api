namespace EFootwear.Domain.Entities;

/// <summary>
/// A store of one company. Table: core.stores. Id 0 is the company's head office (not a shop),
/// so a store with IsHeadOffice = true always has Id 0.
/// </summary>
public sealed class Store
{
    public long CompanyId { get; set; }
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsHeadOffice { get; set; }
    public bool IsActive { get; set; } = true;
}
