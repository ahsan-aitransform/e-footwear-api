using E_Footwear_Api.Authorization;
using E_Footwear_Api.Binding;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Catalogue.Services;
using EFootwear.BLL.Common;
using EFootwear.BLL.Common.Spreadsheet;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Catalogue;

/// <summary>Replace Barcodes (docx 4.13), the barcode history and the data of the Generate Bar Codes labels.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Barcodes.Root)]
public sealed class BarcodesController(BarcodeService barcodeService) : ControllerBase
{
    private const long UploadSizeLimitBytes = SpreadsheetReader.MaximumFileBytes + 64 * 1024;

    /// <summary>"Import From Excel": reads an .xlsx or .csv file (column A = old barcode, column B = new barcode) and checks every row. Nothing is saved.</summary>
    [HttpPost(ApiRoutes.Barcodes.ReplacementFile)]
    [RequirePermission(PermissionCodes.Products.ReplaceBarcodes.View)]
    [RequestSizeLimit(UploadSizeLimitBytes)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<BarcodeReplacementPreviewResponse>> PreviewFile(IFormFile file, CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();
        return await barcodeService.PreviewFromFileAsync(content, file.FileName, cancellationToken);
    }

    /// <summary>Checks pairs typed or edited on the screen. Nothing is saved.</summary>
    [HttpPost(ApiRoutes.Barcodes.ReplacementPreview)]
    [RequirePermission(PermissionCodes.Products.ReplaceBarcodes.View)]
    public async Task<ActionResult<BarcodeReplacementPreviewResponse>> Preview(ReplaceBarcodesRequest request, CancellationToken cancellationToken) =>
        await barcodeService.PreviewAsync(request.Pairs, cancellationToken);

    [HttpPost(ApiRoutes.Barcodes.Replace)]
    [RequirePermission(PermissionCodes.Products.ReplaceBarcodes.Edit)]
    public async Task<ActionResult<BarcodesReplacedResponse>> Replace(ReplaceBarcodesRequest request, CancellationToken cancellationToken) =>
        await barcodeService.ReplaceAsync(request, cancellationToken);

    /// <summary>?barcode=... (old or new) or ?variantId=... ; newest first.</summary>
    [HttpGet(ApiRoutes.Barcodes.History)]
    [RequirePermission(PermissionCodes.Products.ReplaceBarcodes.View)]
    public async Task<ActionResult<PagedResult<BarcodeHistoryEntryResponse>>> ListHistory(
        [FromQuery] PageQuery pageQuery, [FromQuery] string? barcode, [FromQuery] Guid? variantId, CancellationToken cancellationToken) =>
        await barcodeService.ListHistoryAsync(barcode, variantId, pageQuery.ToPageRequest(), cancellationToken);

    /// <summary>The data of the labels to print. The screen draws the barcodes and sends the page to the printer.</summary>
    [HttpPost(ApiRoutes.Barcodes.Labels)]
    [RequirePermission(PermissionCodes.Products.GenerateBarcodes.View)]
    public async Task<ActionResult<IReadOnlyList<BarcodeLabelResponse>>> BuildLabels(BarcodeLabelRequest request, CancellationToken cancellationToken) =>
        Ok(await barcodeService.BuildLabelsAsync(request, cancellationToken));
}
