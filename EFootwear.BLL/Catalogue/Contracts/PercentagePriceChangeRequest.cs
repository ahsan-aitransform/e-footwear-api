using System.Text.Json.Serialization;

namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>
/// Change Sale Price (In %), docx 4.8: raise or lower the sale price of the filtered products by a percentage. Exactly one of
/// IncreasePercent and DecreasePercent is given. The new price is rounded to whole rupees with the company's rounding mode (FLOOR or NEAREST).
/// With a StoreId the store price changes, without one the company list price. CategoryIds and BrandIds match any of the listed ones;
/// null or empty means all. An unknown field is refused, because silently ignoring a filter would change the price of far more products.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PercentagePriceChangeRequest(
    Guid? StoreId,
    Guid? DepartmentId,
    IReadOnlyList<Guid>? CategoryIds,
    IReadOnlyList<Guid>? BrandIds,
    decimal? IncreasePercent,
    decimal? DecreasePercent,
    Guid? ClientTransactionId);
