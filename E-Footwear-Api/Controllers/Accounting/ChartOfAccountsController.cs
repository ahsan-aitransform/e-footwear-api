using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Accounting.Contracts;
using EFootwear.BLL.Accounting.Services;
using EFootwear.BLL.Common;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Accounting;

/// <summary>
/// Chart of accounts master data: GL-Accounts (docx 2.1), New Second Level (2.2) and View Second Level (2.3). The list of groups is
/// open to every company user because the group titles feed the dropdowns of several screens; everything that changes data needs
/// the permission of its screen.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.ChartOfAccounts.Root)]
public sealed class ChartOfAccountsController(ChartOfAccountsService chartOfAccountsService) : ControllerBase
{
    [HttpGet(ApiRoutes.ChartOfAccounts.MainTypes)]
    public ActionResult<IReadOnlyList<AccountMainTypeResponse>> ListMainTypes() => Ok(chartOfAccountsService.ListMainTypes());

    // ----------------------------------------------------------------- groups

    [HttpGet(ApiRoutes.ChartOfAccounts.Groups)]
    public async Task<ActionResult<IReadOnlyList<AccountGroupResponse>>> ListGroups([FromQuery] string? mainTypeCode, CancellationToken cancellationToken) =>
        Ok(await chartOfAccountsService.ListGroupsAsync(mainTypeCode, cancellationToken));

    [HttpGet(ApiRoutes.ChartOfAccounts.NextGroupCode)]
    [RequirePermission(PermissionCodes.Settings.NewSecondLevel.Add)]
    public async Task<ActionResult<NextAccountGroupCodeResponse>> GetNextGroupCode([FromQuery] string mainTypeCode, CancellationToken cancellationToken) =>
        Ok(await chartOfAccountsService.GetNextGroupCodeAsync(mainTypeCode, cancellationToken));

    [HttpPost(ApiRoutes.ChartOfAccounts.Groups)]
    [RequirePermission(PermissionCodes.Settings.NewSecondLevel.Add)]
    public async Task<ActionResult<NextAccountGroupCodeResponse>> CreateGroup(CreateAccountGroupRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await chartOfAccountsService.CreateGroupAsync(request, cancellationToken));

    [HttpPut(ApiRoutes.ChartOfAccounts.GroupByCode)]
    [RequirePermission(PermissionCodes.Settings.ViewSecondLevel.Edit)]
    public async Task<IActionResult> UpdateGroup(string groupCode, UpdateAccountGroupRequest request, CancellationToken cancellationToken)
    {
        await chartOfAccountsService.UpdateGroupAsync(groupCode, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete(ApiRoutes.ChartOfAccounts.GroupByCode)]
    [RequirePermission(PermissionCodes.Settings.ViewSecondLevel.Delete)]
    public async Task<IActionResult> DeleteGroup(string groupCode, CancellationToken cancellationToken)
    {
        await chartOfAccountsService.DeleteGroupAsync(groupCode, cancellationToken);
        return NoContent();
    }

    // --------------------------------------------------------------- accounts

    [HttpGet(ApiRoutes.ChartOfAccounts.Accounts)]
    [RequirePermission(PermissionCodes.Settings.GlAccounts.View)]
    public async Task<ActionResult<PagedResult<AccountResponse>>> ListAccounts(
        [FromQuery] string? groupCode, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await chartOfAccountsService.ListAccountsAsync(groupCode, pageRequest, cancellationToken));

    [HttpGet(ApiRoutes.ChartOfAccounts.NextAccountCode)]
    [RequirePermission(PermissionCodes.Settings.GlAccounts.Add)]
    public async Task<ActionResult<NextAccountCodeResponse>> GetNextAccountCode([FromQuery] string groupCode, CancellationToken cancellationToken) =>
        Ok(await chartOfAccountsService.GetNextAccountCodeAsync(groupCode, cancellationToken));

    [HttpPost(ApiRoutes.ChartOfAccounts.Accounts)]
    [RequirePermission(PermissionCodes.Settings.GlAccounts.Add)]
    public async Task<ActionResult<CreatedIdResponse>> CreateAccount(CreateAccountRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await chartOfAccountsService.CreateAccountAsync(request, cancellationToken));

    [HttpPut(ApiRoutes.ChartOfAccounts.AccountById)]
    [RequirePermission(PermissionCodes.Settings.GlAccounts.Edit)]
    public async Task<IActionResult> UpdateAccount(Guid accountId, UpdateAccountRequest request, CancellationToken cancellationToken)
    {
        await chartOfAccountsService.UpdateAccountAsync(accountId, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete(ApiRoutes.ChartOfAccounts.AccountById)]
    [RequirePermission(PermissionCodes.Settings.GlAccounts.Delete)]
    public async Task<IActionResult> DeleteAccount(Guid accountId, CancellationToken cancellationToken)
    {
        await chartOfAccountsService.DeleteAccountAsync(accountId, cancellationToken);
        return NoContent();
    }
}
