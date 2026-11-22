namespace EFootwear.Domain.Catalogue;

/// <summary>
/// The tables that hang off a product or a size and belong to the catalogue itself. When a product or a size is deleted, rows in these tables
/// do not count as "still in use": they are the product's own prices, history and pictures. Anything else that points at it (stock, invoices,
/// discount rules, vendor articles ...) does, and blocks the delete.
/// </summary>
public static class CatalogueOwnedTables
{
    public const string ProductsTable = "catalog.products";
    public const string VariantsTable = "catalog.variants";

    /// <summary>
    /// Tables that may refer to a size without blocking its deletion: its own prices and history, and the pricing engine's output
    /// (promo.effective_prices and the conflict records are rebuilt by the engine, which drops the rows of deleted sizes).
    /// </summary>
    public static readonly IReadOnlyCollection<string> OwnedByVariant =
    [
        "catalog.variant_prices", "catalog.store_price_overrides", "catalog.price_events", "catalog.barcode_history",
        "promo.effective_prices", "promo.conflict_groups",
    ];

    /// <summary>Tables that may refer to a product without blocking its deletion (its sizes are checked one by one).</summary>
    public static readonly IReadOnlyCollection<string> OwnedByProduct = ["catalog.variants", "catalog.product_images"];
}
