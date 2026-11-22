namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>The answer to saving a product: its id and the code the system allocated (shown on the New Product screen).</summary>
public sealed record ProductSavedResponse(Guid ProductId, string Code);
