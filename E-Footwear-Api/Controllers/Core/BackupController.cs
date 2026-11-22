using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Platform.Contracts;
using EFootwear.BLL.Platform.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Core;

/// <summary>
/// Backup and restore information for a company (docx 2.20). It shows what the operator backups recorded; a company can never
/// start a restore from here (a restore must not touch other tenants' data, so it is an operator procedure).
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Backup.Root)]
public sealed class BackupController(BackupStatusService backupStatusService) : ControllerBase
{
    [HttpGet(ApiRoutes.Backup.Overview)]
    [RequirePermission(PermissionCodes.Settings.Backup.View)]
    public async Task<ActionResult<CompanyBackupOverviewResponse>> GetOverview(CancellationToken cancellationToken) =>
        await backupStatusService.GetCompanyOverviewAsync(cancellationToken);

    [HttpGet(ApiRoutes.Backup.RestoreOverview)]
    [RequirePermission(PermissionCodes.Settings.Restore.View)]
    public async Task<ActionResult<RestoreOverviewResponse>> GetRestoreOverview(CancellationToken cancellationToken) =>
        await backupStatusService.GetRestoreOverviewAsync(cancellationToken);
}
