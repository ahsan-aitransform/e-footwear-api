namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>
/// Change Prices (Old to New), docx 4.7: every size of the filtered products whose price equals OldPrice gets NewPrice. PriceType says which
/// price (the docx does not say, so the screen asks: "SALE" or "PURCHASE"). With a StoreId the sale price of that store changes (a store
/// price); without one the company list price changes. The same request is used to preview and to apply; ClientTransactionId makes a retry safe.
/// </summary>
public sealed record OldToNewPriceChangeRequest(
    string PriceType,
    Guid? StoreId,
    Guid? DepartmentId,
    Guid? CategoryId,
    Guid? BrandId,
    long OldPrice,
    long NewPrice,
    Guid? ClientTransactionId);
