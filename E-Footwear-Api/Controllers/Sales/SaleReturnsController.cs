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

/// <summary>Sale Return (docx 5.2, Part 12): find the original invoice by its number and post the return against its lines.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Sales.Root)]
public sealed class SaleReturnsController(
    SaleEntryService saleEntryService,
    SaleInvoiceQueryService saleInvoiceQueryService,
    SaleInvoicePostingService saleInvoicePostingService) : ControllerBase
{
    [HttpGet(ApiRoutes.Sales.SaleReturnEntryContext)]
    [RequirePermission(PermissionCodes.Invoice.SaleReturn.View)]
    public async Task<ActionResult<SaleEntryContextResponse>> GetEntryContext([FromQuery] Guid storeId, [FromQuery] DateOnly? businessDate, CancellationToken cancellationToken) =>
        Ok(await saleEntryService.GetEntryContextAsync(storeId, businessDate, cancellationToken));

    [HttpGet(ApiRoutes.Sales.SaleReturnInvoiceByNumber)]
    [RequirePermission(PermissionCodes.Invoice.SaleReturn.View)]
    public async Task<ActionResult<ReturnableInvoiceResponse>> FindReturnableInvoice([FromQuery] Guid storeId, [FromQuery] long invoiceNumber, CancellationToken cancellationToken) =>
        Ok(await saleInvoiceQueryService.FindReturnableInvoiceAsync(storeId, invoiceNumber, cancellationToken));

    [HttpPost(ApiRoutes.Sales.SaleReturns)]
    [RequirePermission(PermissionCodes.Invoice.SaleReturn.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Sales.SaleReturn)]
    public async Task<ActionResult<SaleInvoiceResponse>> CreateReturn(CreateSaleReturnRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await saleInvoicePostingService.CreateReturnAsync(request, cancellationToken));
}
