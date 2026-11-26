namespace EFootwear.BLL.Promotions.Contracts;

/// <summary>
/// Adds products to an offer at one discount percentage: one scanned <see cref="Barcode"/>, or every size of a whole <see cref="CategoryId"/> or <see cref="BrandId"/> ("Company")
/// as they exist now (products made later are not added). Give exactly one of the three. A size already in the offer is skipped, never doubled.
/// </summary>
public sealed record AddSpecialOfferLinesRequest(string? Barcode, Guid? CategoryId, Guid? BrandId, decimal? DiscountPercent);
