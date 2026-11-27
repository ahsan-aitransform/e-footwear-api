using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Catalogue.Services;
using EFootwear.BLL.Common.Spreadsheet;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Catalogue;

/// <summary>New Product: create products from an Excel file. The same right as adding a product by hand.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Products.Root)]
public sealed class ProductImportController(ProductImportService productImportService) : ControllerBase
{
    private const long UploadSizeLimitBytes = SpreadsheetReader.MaximumFileBytes + 64 * 1024;

    [HttpGet(ApiRoutes.Products.ImportTemplate)]
    [RequirePermission(PermissionCodes.Products.NewProduct.Add)]
    public IActionResult GetTemplate() => File(ProductImportService.BuildTemplate(), SpreadsheetWriter.ContentType, ProductImportService.TemplateFileName);

    [HttpPost(ApiRoutes.Products.ImportPreview)]
    [RequirePermission(PermissionCodes.Products.NewProduct.Add)]
    [RequestSizeLimit(UploadSizeLimitBytes)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ProductImportResponse>> Preview(IFormFile file, CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();
        return await productImportService.PreviewAsync(content, file.FileName, cancellationToken);
    }

    [HttpPost(ApiRoutes.Products.Import)]
    [RequirePermission(PermissionCodes.Products.NewProduct.Add)]
    [RequestSizeLimit(UploadSizeLimitBytes)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ProductImportResponse>> Import(IFormFile file, CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();
        return await productImportService.ImportAsync(content, file.FileName, cancellationToken);
    }
}
