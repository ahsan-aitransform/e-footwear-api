namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>
/// Change Sale Price (In %), docx 4.8: raise or lower the sale price of the filtered products by a percentage. Exactly one of
/// IncreasePercent and DecreasePercent is given. The new price is rounded to whole rupees with the company's rounding mode (FLOOR or NEAREST).
/// With a StoreId the store price changes, without one the company list price.
/// </summary>
public sealed record PercentagePriceChangeRequest(
    Guid? StoreId,
    Guid? DepartmentId,
    Guid? CategoryId,
    Guid? BrandId,
    decimal? IncreasePercent,
    decimal? DecreasePercent,
    Guid? ClientTransactionId);
