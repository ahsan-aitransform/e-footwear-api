using System.Text.Json.Serialization;

namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>
/// Change Discounts, docx 4.9: give the matching products of one store a discount, a percentage (DiscountType PERCENT) or whole rupees off each
/// piece (AMOUNT). Department, categories, brands and the stock range are optional and narrow the products; null or empty means all. It becomes
/// one product-discount rule for the store; with a stock range the pricing engine applies it only to the sizes whose stock is in the range.
/// The preview needs no discount; applying does. An unknown field is refused, so that a filter is never silently ignored.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StockRangeDiscountRequest(
    Guid StoreId,
    Guid? DepartmentId,
    IReadOnlyList<Guid>? CategoryIds,
    IReadOnlyList<Guid>? BrandIds,
    decimal? MinimumStock,
    decimal? MaximumStock,
    string? DiscountType,
    decimal? DiscountValue,
    Guid? ClientTransactionId);
