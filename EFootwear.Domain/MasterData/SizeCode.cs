namespace EFootwear.Domain.MasterData;

/// <summary>
/// One size inside a size group (for example "40" in "Men shoes EU"). Table: master.sizes. The size groups and their sizes keep the
/// footwear size runs out of the product code, so another retail category can define its own (S, M, L ...): nothing here is
/// footwear specific (Part 7: do not hard-code footwear attributes). Sizes are reference data and have no soft-delete columns.
/// </summary>
public sealed class SizeCode
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public Guid SizeGroupId { get; set; }
    public string Code { get; set; } = "";
    public int SortOrder { get; set; }
}
