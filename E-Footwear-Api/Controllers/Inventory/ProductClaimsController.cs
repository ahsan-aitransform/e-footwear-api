using E_Footwear_Api.Authorization;
using E_Footwear_Api.Binding;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Inventory.Contracts;
using EFootwear.BLL.Inventory.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Inventory;

/// <summary>Product Claim (docx 4.16), Claim Approval (docx 4.17) and the Claim Report (docx 8.31). Approving also needs the legacy CLAIM_APPROVE code, which the baseline function checks again.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Inventory.Root)]
public sealed class ProductClaimsController(
    ProductClaimService productClaimService,
    InventoryLookupService inventoryLookupService,
    InventoryReportService inventoryReportService) : ControllerBase
{
    [HttpGet(ApiRoutes.Inventory.ProductClaims)]
    [RequirePermission(PermissionCodes.Products.ProductClaim.View)]
    public async Task<ActionResult<PagedResult<ProductClaimResponse>>> Search(
        [FromQuery] ProductClaimSearchFilter filter, [FromQuery] PageQuery pageQuery, CancellationToken cancellationToken) =>
        Ok(await productClaimService.SearchAsync(filter, pageQuery.ToPageRequest(), cancellationToken));

    [HttpPost(ApiRoutes.Inventory.ProductClaims)]
    [RequirePermission(PermissionCodes.Products.ProductClaim.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Stock.ClaimRequest)]
    public async Task<ActionResult<ProductClaimResponse>> Create(CreateProductClaimRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await productClaimService.CreateAsync(request, cancellationToken));

    [HttpGet(ApiRoutes.Inventory.ProductClaimItemByBarcode)]
    [RequirePermission(PermissionCodes.Products.ProductClaim.View)]
    public async Task<ActionResult<InventoryItemResponse>> FindItemByBarcode([FromQuery] Guid storeId, [FromQuery] string? barcode, CancellationToken cancellationToken) =>
        Ok(await inventoryLookupService.FindItemByBarcodeAsync(storeId, barcode, cancellationToken));

    [HttpGet(ApiRoutes.Inventory.ProductClaimItemSearch)]
    [RequirePermission(PermissionCodes.Products.ProductClaim.View)]
    public async Task<ActionResult<IReadOnlyList<InventoryItemResponse>>> SearchItems([FromQuery] Guid storeId, [FromQuery] string? searchText, CancellationToken cancellationToken) =>
        Ok(await inventoryLookupService.SearchItemsAsync(storeId, searchText, cancellationToken));

    // ---- claim approval

    [HttpGet(ApiRoutes.Inventory.ClaimApprovals)]
    [RequirePermission(PermissionCodes.Products.ClaimApproval.View)]
    public async Task<ActionResult<PagedResult<ProductClaimResponse>>> SearchForApproval(
        [FromQuery] ProductClaimSearchFilter filter, [FromQuery] PageQuery pageQuery, CancellationToken cancellationToken) =>
        Ok(await productClaimService.SearchAsync(filter, pageQuery.ToPageRequest(), cancellationToken));

    [HttpPost(ApiRoutes.Inventory.ClaimApprovalApprove)]
    [RequirePermission(PermissionCodes.Products.ClaimApproval.Approve)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Stock.ClaimApprove)]
    public async Task<ActionResult<ApproveProductClaimsResponse>> Approve(ApproveProductClaimsRequest request, CancellationToken cancellationToken) =>
        Ok(await productClaimService.ApproveAsync(request, cancellationToken));

    [HttpPost(ApiRoutes.Inventory.ClaimApprovalReject)]
    [RequirePermission(PermissionCodes.Products.ClaimApproval.Approve)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Stock.ClaimApprove)]
    public async Task<ActionResult<ProductClaimResponse>> Reject(Guid claimId, RejectProductClaimRequest request, CancellationToken cancellationToken) =>
        Ok(await productClaimService.RejectAsync(claimId, request, cancellationToken));

    // ---- claim report

    [HttpGet(ApiRoutes.Inventory.ProductClaimReport)]
    [RequirePermission(PermissionCodes.Reports.ClaimReport.View)]
    public async Task<ActionResult<ProductClaimReportResponse>> GetReport([FromQuery] ProductClaimSearchFilter filter, CancellationToken cancellationToken) =>
        Ok(await inventoryReportService.GetClaimReportAsync(filter, cancellationToken));
}
