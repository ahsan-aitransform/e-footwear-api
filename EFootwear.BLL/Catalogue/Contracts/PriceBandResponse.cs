namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>A price band: a named range of list prices. A missing maximum means no upper limit.</summary>
public sealed record PriceBandResponse(Guid Id, string Name, long MinimumPrice, long? MaximumPrice, int SortOrder, long RowVersion);
