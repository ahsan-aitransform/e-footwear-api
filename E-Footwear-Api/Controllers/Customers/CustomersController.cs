using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Customers.Contracts;
using EFootwear.BLL.Customers.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Customers;

/// <summary>
/// Customers: New Customer, View Customers and Search Customer. View and Search are two screens with their own permission, so each has its own list
/// endpoint over the same use case. The "lookup" endpoint gives a light list for dropdowns on other screens to every company user.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Customers.Root)]
public sealed class CustomersController(CustomerService customerService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.Customers.ViewCustomers.View)]
    public async Task<ActionResult<PagedResult<CustomerListItemResponse>>> List(
        [FromQuery] CustomerSearchRequest filter, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await customerService.SearchAsync(filter, pageRequest, cancellationToken));

    [HttpGet(ApiRoutes.Customers.Search)]
    [RequirePermission(PermissionCodes.Customers.SearchCustomer.View)]
    public async Task<ActionResult<PagedResult<CustomerListItemResponse>>> Search(
        [FromQuery] CustomerSearchRequest filter, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await customerService.SearchAsync(filter, pageRequest, cancellationToken));

    [HttpGet(ApiRoutes.Customers.Lookup)]
    public async Task<ActionResult<IReadOnlyList<CustomerLookupItemResponse>>> Lookup([FromQuery] string? searchText, CancellationToken cancellationToken) =>
        Ok(await customerService.LookupAsync(searchText, cancellationToken));

    [HttpGet(ApiRoutes.Customers.CustomerById)]
    [RequirePermission(PermissionCodes.Customers.NewCustomer.View)]
    public async Task<ActionResult<CustomerDetailResponse>> Get(Guid customerId, CancellationToken cancellationToken) =>
        Ok(await customerService.GetAsync(customerId, cancellationToken));

    [HttpPost]
    [RequirePermission(PermissionCodes.Customers.NewCustomer.Add)]
    public async Task<ActionResult<CreatedIdResponse>> Create(CreateCustomerRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await customerService.CreateAsync(request, cancellationToken));

    [HttpPut(ApiRoutes.Customers.CustomerById)]
    [RequirePermission(PermissionCodes.Customers.NewCustomer.Edit)]
    public async Task<IActionResult> Update(Guid customerId, UpdateCustomerRequest request, CancellationToken cancellationToken)
    {
        await customerService.UpdateAsync(customerId, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete(ApiRoutes.Customers.CustomerById)]
    [RequirePermission(PermissionCodes.Customers.ViewCustomers.Delete)]
    public async Task<IActionResult> Delete(Guid customerId, CancellationToken cancellationToken)
    {
        await customerService.DeleteAsync(customerId, cancellationToken);
        return NoContent();
    }
}
