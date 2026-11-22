namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>Product Discounts On Multiple Stores, docx 4.11: set one discount percentage for a product in every selected store. A discount the product already had in those stores is replaced.</summary>
public sealed record ProductDiscountOnStoresRequest(
    Guid ProductId,
    IReadOnlyList<Guid> StoreIds,
    decimal DiscountPercent,
    Guid? ClientTransactionId);
