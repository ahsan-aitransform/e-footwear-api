namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>Asks for the data of the Generate Bar Codes labels. StoreId chooses whose price is printed (without it the company list price).</summary>
public sealed record BarcodeLabelRequest(IReadOnlyList<BarcodeLabelItemRequest> Items, Guid? StoreId);
