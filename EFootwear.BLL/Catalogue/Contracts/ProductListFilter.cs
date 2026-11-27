namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>
/// What View Products (docx 4.2) sends. Every filter is optional (default "All"). StoreId selects the store whose stock, store prices and discounts
/// are shown; without it the stock is the total of the stores the caller may see and the price is the company list price. The typed search text is
/// looked for in the name, the product code and the barcode.
/// </summary>
public sealed record ProductListFilter(
    Guid? StoreId = null,
    Guid? DepartmentId = null,
    Guid? CategoryId = null,
    Guid? BrandId = null,
    Guid? GenderId = null,
    Guid? SeasonId = null,
    Guid? ColorId = null,
    bool OnlyActive = false);
