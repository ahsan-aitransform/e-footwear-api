using EFootwear.Domain.Common;

namespace EFootwear.Domain.Catalogue;

/// <summary>
/// An owner-defined price range (catalog.price_bands) used by price-band analytics and reports. Ranges of one company may not overlap
/// (a database exclusion constraint); a missing maximum means no upper limit. Whole rupees. Rows are soft-deleted.
/// </summary>
public sealed class PriceBand : IHasRowVersion
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public long MinPrice { get; set; }
    public long? MaxPrice { get; set; }
    public int SortOrder { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public long RowVersion { get; set; }
}
