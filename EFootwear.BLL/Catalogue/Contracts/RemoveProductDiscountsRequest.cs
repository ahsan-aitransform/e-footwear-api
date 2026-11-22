namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>
/// Remove Discounts and Reverse Price, docx 4.12. The discounts that the product screens gave the products in the store are ended, so the
/// price goes back to the store (or list) price. AlsoRestorePreviousSalePrice additionally puts back the sale price before the latest price
/// change of each size, but only where that is clear (see the response); the docx does not say when the reversal is meant, so it is the user's choice.
/// </summary>
public sealed record RemoveProductDiscountsRequest(
    Guid StoreId,
    IReadOnlyList<Guid> ProductIds,
    bool AlsoRestorePreviousSalePrice,
    Guid? ClientTransactionId);
