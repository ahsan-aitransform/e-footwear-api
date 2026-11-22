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

/// <summary>Change Product Attributes (docx 4.6): list the products that have a value, then move them all to a new value.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.ProductAttributes.Root)]
public sealed class ProductAttributesController(ProductAttributeService productAttributeService) : ControllerBase
{
    /// <summary>?fieldType=BRAND&amp;oldValueId=... (UNIT, BRAND, CATEGORY, GENDER, SEASON, COLOR, LOCATION, PRODUCT_TYPE)</summary>
    [HttpGet(ApiRoutes.ProductAttributes.ProductsWithValue)]
    [RequirePermission(PermissionCodes.Products.ChangeAttributes.View)]
    public async Task<ActionResult<PagedResult<ProductAttributeRowResponse>>> ListProducts(
        [FromQuery] string? fieldType, [FromQuery] Guid oldValueId, [FromQuery] PageQuery pageQuery, CancellationToken cancellationToken) =>
        await productAttributeService.ListProductsAsync(fieldType, oldValueId, pageQuery.ToPageRequest(), cancellationToken);

    [HttpPost(ApiRoutes.ProductAttributes.Apply)]
    [RequirePermission(PermissionCodes.Products.ChangeAttributes.Edit)]
    public async Task<ActionResult<AttributeChangeAppliedResponse>> Apply(ChangeProductAttributeRequest request, CancellationToken cancellationToken) =>
        await productAttributeService.ApplyAsync(request, cancellationToken);
}
