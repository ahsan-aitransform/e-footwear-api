namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>Creates or updates a price band. RowVersion is only used on update.</summary>
public sealed record SavePriceBandRequest(string Name, long MinimumPrice, long? MaximumPrice, int SortOrder, long? RowVersion);
