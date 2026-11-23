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

/// <summary>
/// View Invoices (the list, the detail, printing, void and "void and re-enter" as the edit of a posted sale) and View Deleted Invoices (docx 5.13). Voiding needs
/// the Delete permission of View Invoices; "void and re-enter" needs Edit and Delete of View Invoices and the right to make a new sale.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Sales.Root)]
public sealed class SalesInvoicesController(
    SaleInvoiceQueryService saleInvoiceQueryService,
    SaleInvoicePostingService saleInvoicePostingService,
    SaleEntryService saleEntryService) : ControllerBase
{
    [HttpGet(ApiRoutes.Sales.Invoices)]
    [RequirePermission(PermissionCodes.Invoice.ViewInvoices.View)]
    public async Task<ActionResult<PagedResult<SaleInvoiceListItemResponse>>> Search(
        [FromQuery] SaleInvoiceSearchFilter filter, [FromQuery] PageQuery pageQuery, CancellationToken cancellationToken) =>
        Ok(await saleInvoiceQueryService.SearchAsync(filter, pageQuery.ToPageRequest(), cancellationToken));

    [HttpGet(ApiRoutes.Sales.InvoiceById)]
    [RequirePermission(PermissionCodes.Invoice.ViewInvoices.View)]
    public async Task<ActionResult<SaleInvoiceResponse>> Get(Guid invoiceId, CancellationToken cancellationToken) =>
        Ok(await saleInvoiceQueryService.GetAsync(invoiceId, cancellationToken));

    [HttpGet(ApiRoutes.Sales.InvoicePrintDocument)]
    [RequirePermission(PermissionCodes.Invoice.ViewInvoices.Print)]
    public async Task<ActionResult<SalePrintDocumentResponse>> GetPrintDocument(Guid invoiceId, CancellationToken cancellationToken) =>
        Ok(await saleInvoiceQueryService.GetPrintDocumentAsync(invoiceId, cancellationToken));

    [HttpGet(ApiRoutes.Sales.InvoiceCustomers)]
    [RequirePermission(PermissionCodes.Invoice.ViewInvoices.View)]
    public async Task<ActionResult<IReadOnlyList<SaleCustomerLookupResponse>>> SearchCustomers([FromQuery] string? searchText, CancellationToken cancellationToken) =>
        Ok(await saleEntryService.SearchCustomersAsync(searchText, cancellationToken));

    [HttpGet(ApiRoutes.Sales.InvoiceSalesmen)]
    [RequirePermission(PermissionCodes.Invoice.ViewInvoices.View)]
    public async Task<ActionResult<IReadOnlyList<SaleSalesmanLookupResponse>>> ListSalesmen([FromQuery] Guid storeId, CancellationToken cancellationToken) =>
        Ok(await saleEntryService.ListSalesmenAsync(storeId, cancellationToken));

    [HttpPost(ApiRoutes.Sales.InvoiceVoid)]
    [RequirePermission(PermissionCodes.Invoice.ViewInvoices.Delete)]
    public async Task<ActionResult<VoidSaleInvoiceResponse>> Void(Guid invoiceId, VoidSaleInvoiceRequest request, CancellationToken cancellationToken) =>
        Ok(await saleInvoicePostingService.VoidAsync(invoiceId, request, cancellationToken));

    [HttpPost(ApiRoutes.Sales.InvoiceVoidAndReenter)]
    [RequirePermission(PermissionCodes.Invoice.ViewInvoices.Edit)]
    [RequirePermission(PermissionCodes.Invoice.ViewInvoices.Delete)]
    [RequirePermission(PermissionCodes.Invoice.NewSale.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Sales.SaleCreate)]
    public async Task<ActionResult<VoidAndReenterSaleResponse>> VoidAndReenter(Guid invoiceId, VoidAndReenterSaleRequest request, CancellationToken cancellationToken) =>
        Ok(await saleInvoicePostingService.VoidAndReenterAsync(invoiceId, request, cancellationToken));

    // ------------------------------------------------------------ View Deleted Invoices (5.13)

    [HttpGet(ApiRoutes.Sales.DeletedInvoices)]
    [RequirePermission(PermissionCodes.Invoice.ViewDeletedInvoices.View)]
    public async Task<ActionResult<PagedResult<SaleInvoiceListItemResponse>>> SearchDeleted(
        [FromQuery] SaleInvoiceSearchFilter filter, [FromQuery] PageQuery pageQuery, CancellationToken cancellationToken) =>
        Ok(await saleInvoiceQueryService.SearchDeletedAsync(filter, pageQuery.ToPageRequest(), cancellationToken));

    [HttpGet(ApiRoutes.Sales.DeletedInvoiceById)]
    [RequirePermission(PermissionCodes.Invoice.ViewDeletedInvoices.View)]
    public async Task<ActionResult<SaleInvoiceResponse>> GetDeleted(Guid invoiceId, CancellationToken cancellationToken) =>
        Ok(await saleInvoiceQueryService.GetDeletedAsync(invoiceId, cancellationToken));

    [HttpGet(ApiRoutes.Sales.DeletedInvoicePrintDocument)]
    [RequirePermission(PermissionCodes.Invoice.ViewDeletedInvoices.Print)]
    public async Task<ActionResult<SalePrintDocumentResponse>> GetDeletedPrintDocument(Guid invoiceId, CancellationToken cancellationToken)
    {
        await saleInvoiceQueryService.GetDeletedAsync(invoiceId, cancellationToken);
        return Ok(await saleInvoiceQueryService.GetPrintDocumentAsync(invoiceId, cancellationToken));
    }
}
