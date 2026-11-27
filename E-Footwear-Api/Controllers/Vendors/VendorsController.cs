using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Vendors.Contracts;
using EFootwear.BLL.Vendors.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Vendors;

/// <summary>
/// Vendors: New Vendor and View Vendors. The "lookup" endpoint gives a light list
/// for dropdowns on other screens to every company user.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Vendors.Root)]
public sealed class VendorsController(VendorService vendorService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.Vendors.ViewVendors.View)]
    public async Task<ActionResult<PagedResult<VendorListItemResponse>>> List(
        [FromQuery] VendorSearchRequest filter, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await vendorService.SearchAsync(filter, pageRequest, cancellationToken));

    [HttpGet(ApiRoutes.Vendors.Lookup)]
    public async Task<ActionResult<IReadOnlyList<VendorLookupItemResponse>>> Lookup([FromQuery] string? searchText, CancellationToken cancellationToken) =>
        Ok(await vendorService.LookupAsync(searchText, cancellationToken));

    [HttpGet(ApiRoutes.Vendors.VendorById)]
    [RequirePermission(PermissionCodes.Vendors.NewVendor.View)]
    public async Task<ActionResult<VendorDetailResponse>> Get(Guid vendorId, CancellationToken cancellationToken) =>
        Ok(await vendorService.GetAsync(vendorId, cancellationToken));

    [HttpPost]
    [RequirePermission(PermissionCodes.Vendors.NewVendor.Add)]
    public async Task<ActionResult<CreatedIdResponse>> Create(CreateVendorRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await vendorService.CreateAsync(request, cancellationToken));

    [HttpPut(ApiRoutes.Vendors.VendorById)]
    [RequirePermission(PermissionCodes.Vendors.NewVendor.Edit)]
    public async Task<IActionResult> Update(Guid vendorId, UpdateVendorRequest request, CancellationToken cancellationToken)
    {
        await vendorService.UpdateAsync(vendorId, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete(ApiRoutes.Vendors.VendorById)]
    [RequirePermission(PermissionCodes.Vendors.ViewVendors.Delete)]
    public async Task<IActionResult> Delete(Guid vendorId, CancellationToken cancellationToken)
    {
        await vendorService.DeleteAsync(vendorId, cancellationToken);
        return NoContent();
    }
}
