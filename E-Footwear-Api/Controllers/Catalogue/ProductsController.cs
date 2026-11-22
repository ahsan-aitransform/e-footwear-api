using E_Footwear_Api.Authorization;
using E_Footwear_Api.Binding;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Catalogue.Services;
using EFootwear.BLL.Common;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Catalogue;

/// <summary>
/// Products and their sizes (docx 4.1 New Product, 4.2 View Products, 4.3 Search Product). The four list endpoints return the same rows (one per size) and differ
/// only in the screen permission they need: View Products, Search Product, and the two discount screens that show the same grid.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Products.Root)]
public sealed class ProductsController(ProductCatalogService productCatalogService, ProductListingService productListingService) : ControllerBase
{
    /// <summary>Filters: storeId, departmentId, categoryId, brandId, genderId, seasonId, colorId, onlyActive; paging and free text through the page query.</summary>
    [HttpGet(ApiRoutes.Products.Variants)]
    [RequirePermission(PermissionCodes.Products.ViewProducts.View)]
    public async Task<ActionResult<PagedResult<ProductVariantListItemResponse>>> ListVariants(
        [FromQuery] PageQuery pageQuery, [FromQuery] ProductListFilter filter, CancellationToken cancellationToken) =>
        await productListingService.ListVariantRowsAsync(filter, pageQuery.ToPageRequest(), cancellationToken);

    /// <summary>Search Product: also searchField (NAME, BARCODE, DEPARTMENT, CATEGORY, BRAND, SALE_PRICE, COLOR), matchMode (START, CONTAINS, END), includeDescription.</summary>
    [HttpGet(ApiRoutes.Products.VariantsSearch)]
    [RequirePermission(PermissionCodes.Products.SearchProduct.View)]
    public async Task<ActionResult<PagedResult<ProductVariantListItemResponse>>> SearchVariants(
        [FromQuery] PageQuery pageQuery, [FromQuery] ProductListFilter filter, CancellationToken cancellationToken) =>
        await productListingService.ListVariantRowsAsync(filter, pageQuery.ToPageRequest(), cancellationToken);

    [HttpGet(ApiRoutes.Products.VariantsForDiscounts)]
    [RequirePermission(PermissionCodes.Products.ProductDiscounts.View)]
    public async Task<ActionResult<PagedResult<ProductVariantListItemResponse>>> ListVariantsForDiscounts(
        [FromQuery] PageQuery pageQuery, [FromQuery] ProductListFilter filter, CancellationToken cancellationToken) =>
        await productListingService.ListVariantRowsAsync(filter, pageQuery.ToPageRequest(), cancellationToken);

    [HttpGet(ApiRoutes.Products.VariantsForDiscountRemoval)]
    [RequirePermission(PermissionCodes.Products.RemoveDiscountsAndReversePrice.View)]
    public async Task<ActionResult<PagedResult<ProductVariantListItemResponse>>> ListVariantsForDiscountRemoval(
        [FromQuery] PageQuery pageQuery, [FromQuery] ProductListFilter filter, CancellationToken cancellationToken) =>
        await productListingService.ListVariantRowsAsync(filter, pageQuery.ToPageRequest(), cancellationToken);

    [HttpGet(ApiRoutes.Products.ById)]
    [RequirePermission(PermissionCodes.Products.NewProduct.View)]
    public async Task<ActionResult<ProductDetailResponse>> Get(Guid productId, CancellationToken cancellationToken) =>
        await productCatalogService.GetDetailAsync(productId, cancellationToken);

    /// <summary>The code the next product will get (a preview; the real code is allocated when the product is saved).</summary>
    [HttpGet(ApiRoutes.Products.NextCode)]
    [RequirePermission(PermissionCodes.Products.NewProduct.Add)]
    public async Task<ActionResult<NextProductCodeResponse>> GetNextCode(CancellationToken cancellationToken) =>
        await productCatalogService.GetNextProductCodeAsync(cancellationToken);

    [HttpPost]
    [RequirePermission(PermissionCodes.Products.NewProduct.Add)]
    public async Task<ActionResult<ProductSavedResponse>> Create(CreateProductRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await productCatalogService.CreateAsync(request, cancellationToken));

    /// <summary>Changing the company sale price of an existing size also needs PRODUCTS.NEW_PRODUCT.CHANGE_SELLING_PRICE (checked by the service).</summary>
    [HttpPut(ApiRoutes.Products.ById)]
    [RequirePermission(PermissionCodes.Products.NewProduct.Edit)]
    public async Task<IActionResult> Update(Guid productId, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        await productCatalogService.UpdateAsync(productId, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete(ApiRoutes.Products.ById)]
    [RequirePermission(PermissionCodes.Products.ViewProducts.Delete)]
    public async Task<IActionResult> Delete(Guid productId, CancellationToken cancellationToken)
    {
        await productCatalogService.DeleteAsync(productId, cancellationToken);
        return NoContent();
    }

    [HttpGet(ApiRoutes.Products.PriceHistory)]
    [RequirePermission(PermissionCodes.Products.NewProduct.View)]
    public async Task<ActionResult<PagedResult<PriceHistoryEntryResponse>>> ListPriceHistory(
        Guid productId, [FromQuery] PageQuery pageQuery, CancellationToken cancellationToken) =>
        await productCatalogService.ListPriceHistoryAsync(productId, pageQuery.ToPageRequest(), cancellationToken);
}
