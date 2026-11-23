using E_Footwear_Api.Authorization;
using E_Footwear_Api.Binding;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Sales.Contracts;
using EFootwear.BLL.Sales.Services;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Sales;

/// <summary>New Sale (docx 5.1): the lookups of the entry screen and the posting of a sale. Saving also needs the legacy SALE_CREATE code, which the database function checks again.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Sales.Root)]
public sealed class NewSaleController(SaleEntryService saleEntryService, SaleInvoicePostingService saleInvoicePostingService) : ControllerBase
{
    [HttpGet(ApiRoutes.Sales.NewSaleEntryContext)]
    [RequirePermission(PermissionCodes.Invoice.NewSale.View)]
    public async Task<ActionResult<SaleEntryContextResponse>> GetEntryContext([FromQuery] Guid storeId, [FromQuery] DateOnly? businessDate, CancellationToken cancellationToken) =>
        Ok(await saleEntryService.GetEntryContextAsync(storeId, businessDate, cancellationToken));

    [HttpGet(ApiRoutes.Sales.NewSaleItemByBarcode)]
    [RequirePermission(PermissionCodes.Invoice.NewSale.View)]
    public async Task<ActionResult<SaleItemResponse>> FindItemByBarcode([FromQuery] Guid storeId, [FromQuery] string? barcode, CancellationToken cancellationToken) =>
        Ok(await saleEntryService.FindItemByBarcodeAsync(storeId, barcode, cancellationToken));

    [HttpGet(ApiRoutes.Sales.NewSaleItemSearch)]
    [RequirePermission(PermissionCodes.Invoice.NewSale.View)]
    public async Task<ActionResult<IReadOnlyList<SaleItemResponse>>> SearchItems([FromQuery] Guid storeId, [FromQuery] string? searchText, CancellationToken cancellationToken) =>
        Ok(await saleEntryService.SearchItemsAsync(storeId, searchText, cancellationToken));

    [HttpGet(ApiRoutes.Sales.NewSaleItemPicture)]
    [RequirePermission(PermissionCodes.Invoice.NewSale.View)]
    public async Task<IActionResult> GetItemPicture(Guid productId, CancellationToken cancellationToken)
    {
        var picture = await saleEntryService.OpenItemPictureAsync(productId, cancellationToken)
                      ?? throw new DomainException(ErrorKind.NotFound, "This item has no picture.");
        Response.Headers.CacheControl = "private, no-cache";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(picture.Content, picture.ContentType);
    }

    [HttpGet(ApiRoutes.Sales.NewSaleCustomers)]
    [RequirePermission(PermissionCodes.Invoice.NewSale.View)]
    public async Task<ActionResult<IReadOnlyList<SaleCustomerLookupResponse>>> SearchCustomers([FromQuery] string? searchText, CancellationToken cancellationToken) =>
        Ok(await saleEntryService.SearchCustomersAsync(searchText, cancellationToken));

    [HttpGet(ApiRoutes.Sales.NewSaleSalesmen)]
    [RequirePermission(PermissionCodes.Invoice.NewSale.View)]
    public async Task<ActionResult<IReadOnlyList<SaleSalesmanLookupResponse>>> ListSalesmen([FromQuery] Guid storeId, CancellationToken cancellationToken) =>
        Ok(await saleEntryService.ListSalesmenAsync(storeId, cancellationToken));

    [HttpGet(ApiRoutes.Sales.NewSalePaymentAccounts)]
    [RequirePermission(PermissionCodes.Invoice.NewSale.View)]
    public async Task<ActionResult<IReadOnlyList<SalePaymentAccountResponse>>> ListPaymentAccounts(CancellationToken cancellationToken) =>
        Ok(await saleEntryService.ListPaymentAccountsAsync(cancellationToken));

    [HttpGet(ApiRoutes.Sales.NewSalePosTerminals)]
    [RequirePermission(PermissionCodes.Invoice.NewSale.View)]
    public async Task<ActionResult<IReadOnlyList<SalePosTerminalResponse>>> ListPosTerminals([FromQuery] Guid storeId, CancellationToken cancellationToken) =>
        Ok(await saleEntryService.ListPosTerminalsAsync(storeId, cancellationToken));

    [HttpPost(ApiRoutes.Sales.NewSale)]
    [RequirePermission(PermissionCodes.Invoice.NewSale.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Sales.SaleCreate)]
    public async Task<ActionResult<SaleInvoiceResponse>> CreateSale(CreateSaleInvoiceRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await saleInvoicePostingService.CreateSaleAsync(request, cancellationToken));
}
