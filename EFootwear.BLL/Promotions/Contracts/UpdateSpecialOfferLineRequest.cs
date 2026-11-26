namespace EFootwear.BLL.Promotions.Contracts;

/// <summary>Changes the discount percentage of one line of an offer.</summary>
public sealed record UpdateSpecialOfferLineRequest(decimal? DiscountPercent);
