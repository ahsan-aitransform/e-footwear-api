using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Catalogue.Services;
using EFootwear.Domain.Catalogue;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Catalogue;

/// <summary>The four picture slots of a product (docx 4.1 Images tab). Pictures are only ever served to the company that owns the product.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Products.Root)]
public sealed class ProductImagesController(ProductImageService productImageService) : ControllerBase
{
    // A little more than the picture limit, for the multipart envelope; the service enforces the exact limit on the file itself.
    private const long RequestSizeLimitBytes = ProductImageRules.MaximumSizeBytes + 64 * 1024;

    [HttpPut(ApiRoutes.Products.Image)]
    [RequirePermission(PermissionCodes.Products.NewProduct.Edit)]
    [RequestSizeLimit(RequestSizeLimitBytes)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ProductImageResponse>> Upload(Guid productId, int slot, IFormFile file, CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();
        return await productImageService.UploadAsync(productId, slot, content, cancellationToken);
    }

    [HttpGet(ApiRoutes.Products.Image)]
    [RequirePermission(PermissionCodes.Products.NewProduct.View)]
    public async Task<IActionResult> GetContent(Guid productId, int slot, CancellationToken cancellationToken)
    {
        var image = await productImageService.OpenAsync(productId, slot, cancellationToken)
                    ?? throw new DomainException(ErrorKind.NotFound, "This picture slot is empty.");
        Response.Headers.CacheControl = "private, no-cache";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(image.Content, image.ContentType);
    }

    [HttpDelete(ApiRoutes.Products.Image)]
    [RequirePermission(PermissionCodes.Products.NewProduct.Edit)]
    public async Task<IActionResult> Clear(Guid productId, int slot, CancellationToken cancellationToken)
    {
        await productImageService.ClearAsync(productId, slot, cancellationToken);
        return NoContent();
    }
}
