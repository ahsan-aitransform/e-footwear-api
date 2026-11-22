using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Platform.Contracts;
using EFootwear.BLL.Platform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Platform;

/// <summary>Backup health, backup runs and restore drills of the whole platform. Platform administrators only.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.PlatformAdministratorOnly)]
[Route(ApiRoutes.PlatformBackup.Root)]
public sealed class PlatformBackupController(BackupStatusService backupStatusService) : ControllerBase
{
    [HttpGet(ApiRoutes.PlatformBackup.Overview)]
    public async Task<ActionResult<PlatformBackupOverviewResponse>> GetOverview(CancellationToken cancellationToken) =>
        await backupStatusService.GetPlatformOverviewAsync(cancellationToken);
}
