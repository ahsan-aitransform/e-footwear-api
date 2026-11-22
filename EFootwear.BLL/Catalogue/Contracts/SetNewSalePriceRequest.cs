namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>Product Discounts, docx 4.10: set a new sale price for every size of the selected products (a store price when StoreId is given, else the list price).</summary>
public sealed record SetNewSalePriceRequest(
    Guid? StoreId,
    IReadOnlyList<Guid> ProductIds,
    long NewSalePrice,
    Guid? ClientTransactionId);
