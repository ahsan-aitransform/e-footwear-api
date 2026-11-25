using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.CashManagement.Contracts;
using EFootwear.BLL.CashManagement.Services;
using EFootwear.BLL.Common;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.CashManagement;

/// <summary>Customer promises (docx 3.5). "Delete" cancels a promise; it is never removed.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Promises.Root)]
public sealed class PromisesController(PromiseService promiseService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.Customers.Promise.View)]
    public async Task<ActionResult<PromiseListResponse>> List(
        [FromQuery] PromiseSearchRequest filter, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await promiseService.SearchAsync(filter, pageRequest, cancellationToken));

    [HttpGet(ApiRoutes.Promises.PromiseById)]
    [RequirePermission(PermissionCodes.Customers.Promise.View)]
    public async Task<ActionResult<PromiseResponse>> Get(Guid promiseId, CancellationToken cancellationToken) =>
        Ok(await promiseService.GetAsync(promiseId, cancellationToken));

    [HttpGet(ApiRoutes.Promises.CustomerBalance)]
    [RequirePermission(PermissionCodes.Customers.Promise.View)]
    public async Task<ActionResult<CustomerBalanceResponse>> GetCustomerBalance(Guid customerId, CancellationToken cancellationToken) =>
        Ok(await promiseService.GetCustomerBalanceAsync(customerId, cancellationToken));

    [HttpPost]
    [RequirePermission(PermissionCodes.Customers.Promise.Add)]
    public async Task<ActionResult<CreatedIdResponse>> Create(CreatePromiseRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await promiseService.CreateAsync(request, cancellationToken));

    [HttpPut(ApiRoutes.Promises.PromiseById)]
    [RequirePermission(PermissionCodes.Customers.Promise.Edit)]
    public async Task<IActionResult> Update(Guid promiseId, UpdatePromiseRequest request, CancellationToken cancellationToken)
    {
        await promiseService.UpdateAsync(promiseId, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete(ApiRoutes.Promises.PromiseById)]
    [RequirePermission(PermissionCodes.Customers.Promise.Delete)]
    public async Task<IActionResult> Cancel(Guid promiseId, [FromQuery] long rowVersion, CancellationToken cancellationToken)
    {
        await promiseService.CancelAsync(promiseId, rowVersion, cancellationToken);
        return NoContent();
    }
}
