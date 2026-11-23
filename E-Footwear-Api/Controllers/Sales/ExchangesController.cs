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

/// <summary>Exchange Invoice (docx 5.3): the return of old items and the sale of new ones in one transaction. Needs both the return and the sale legacy codes.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Sales.Root)]
public sealed class ExchangesController(
    SaleEntryService saleEntryService,
    SaleInvoiceQueryService saleInvoiceQueryService,
    SaleInvoicePostingService saleInvoicePostingService) : ControllerBase
{
    [HttpGet(ApiRoutes.Sales.ExchangeEntryContext)]
    [RequirePermission(PermissionCodes.Invoice.ExchangeInvoice.View)]
    public async Task<ActionResult<SaleEntryContextResponse>> GetEntryContext([FromQuery] Guid storeId, [FromQuery] DateOnly? businessDate, CancellationToken cancellationToken) =>
        Ok(await saleEntryService.GetEntryContextAsync(storeId, businessDate, cancellationToken));

    [HttpGet(ApiRoutes.Sales.ExchangeItemByBarcode)]
    [RequirePermission(PermissionCodes.Invoice.ExchangeInvoice.View)]
    public async Task<ActionResult<SaleItemResponse>> FindItemByBarcode([FromQuery] Guid storeId, [FromQuery] string? barcode, CancellationToken cancellationToken) =>
        Ok(await saleEntryService.FindItemByBarcodeAsync(storeId, barcode, cancellationToken));

    [HttpGet(ApiRoutes.Sales.ExchangeItemSearch)]
    [RequirePermission(PermissionCodes.Invoice.ExchangeInvoice.View)]
    public async Task<ActionResult<IReadOnlyList<SaleItemResponse>>> SearchItems([FromQuery] Guid storeId, [FromQuery] string? searchText, CancellationToken cancellationToken) =>
        Ok(await saleEntryService.SearchItemsAsync(storeId, searchText, cancellationToken));

    [HttpGet(ApiRoutes.Sales.ExchangeItemPicture)]
    [RequirePermission(PermissionCodes.Invoice.ExchangeInvoice.View)]
    public async Task<IActionResult> GetItemPicture(Guid productId, CancellationToken cancellationToken)
    {
        var picture = await saleEntryService.OpenItemPictureAsync(productId, cancellationToken)
                      ?? throw new DomainException(ErrorKind.NotFound, "This item has no picture.");
        Response.Headers.CacheControl = "private, no-cache";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(picture.Content, picture.ContentType);
    }

    [HttpGet(ApiRoutes.Sales.ExchangeCustomers)]
    [RequirePermission(PermissionCodes.Invoice.ExchangeInvoice.View)]
    public async Task<ActionResult<IReadOnlyList<SaleCustomerLookupResponse>>> SearchCustomers([FromQuery] string? searchText, CancellationToken cancellationToken) =>
        Ok(await saleEntryService.SearchCustomersAsync(searchText, cancellationToken));

    [HttpGet(ApiRoutes.Sales.ExchangeSalesmen)]
    [RequirePermission(PermissionCodes.Invoice.ExchangeInvoice.View)]
    public async Task<ActionResult<IReadOnlyList<SaleSalesmanLookupResponse>>> ListSalesmen([FromQuery] Guid storeId, CancellationToken cancellationToken) =>
        Ok(await saleEntryService.ListSalesmenAsync(storeId, cancellationToken));

    [HttpGet(ApiRoutes.Sales.ExchangePaymentAccounts)]
    [RequirePermission(PermissionCodes.Invoice.ExchangeInvoice.View)]
    public async Task<ActionResult<IReadOnlyList<SalePaymentAccountResponse>>> ListPaymentAccounts(CancellationToken cancellationToken) =>
        Ok(await saleEntryService.ListPaymentAccountsAsync(cancellationToken));

    [HttpGet(ApiRoutes.Sales.ExchangePosTerminals)]
    [RequirePermission(PermissionCodes.Invoice.ExchangeInvoice.View)]
    public async Task<ActionResult<IReadOnlyList<SalePosTerminalResponse>>> ListPosTerminals([FromQuery] Guid storeId, CancellationToken cancellationToken) =>
        Ok(await saleEntryService.ListPosTerminalsAsync(storeId, cancellationToken));

    [HttpGet(ApiRoutes.Sales.ExchangeInvoiceByNumber)]
    [RequirePermission(PermissionCodes.Invoice.ExchangeInvoice.View)]
    public async Task<ActionResult<ReturnableInvoiceResponse>> FindReturnableInvoice([FromQuery] Guid storeId, [FromQuery] long invoiceNumber, CancellationToken cancellationToken) =>
        Ok(await saleInvoiceQueryService.FindReturnableInvoiceAsync(storeId, invoiceNumber, cancellationToken));

    [HttpPost(ApiRoutes.Sales.Exchanges)]
    [RequirePermission(PermissionCodes.Invoice.ExchangeInvoice.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Sales.SaleReturn)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Sales.SaleCreate)]
    public async Task<ActionResult<ExchangeResponse>> CreateExchange(CreateExchangeRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await saleInvoicePostingService.CreateExchangeAsync(request, cancellationToken));
}
