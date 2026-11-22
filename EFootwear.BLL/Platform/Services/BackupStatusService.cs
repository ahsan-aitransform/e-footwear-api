using EFootwear.BLL.Common;
using EFootwear.BLL.Platform.Abstractions;
using EFootwear.BLL.Platform.Contracts;
using EFootwear.Domain.Common;

namespace EFootwear.BLL.Platform.Services;

/// <summary>
/// Backup and restore information (docx 2.20, ADD-07-06). Backups run outside the application (operator commands, see
/// ARCHITECTURE.md); this service only reports what they recorded. A restore is an operator procedure: a company can never restore
/// itself from the screen because a restore must not touch other tenants' data.
/// </summary>
public sealed class BackupStatusService(IBackupStatusRepository backupStatusRepository, RequestContext requestContext)
{
    public const int ListLimit = 50;

    public async Task<CompanyBackupOverviewResponse> GetCompanyOverviewAsync(CancellationToken cancellationToken) =>
        new(await backupStatusRepository.ReadHealthAsync(cancellationToken),
            await backupStatusRepository.ListCompanyExportsAsync(ListLimit, cancellationToken));

    public async Task<RestoreOverviewResponse> GetRestoreOverviewAsync(CancellationToken cancellationToken) =>
        new(await backupStatusRepository.ReadHealthAsync(cancellationToken),
            await backupStatusRepository.ListRestoreDrillsAsync(ListLimit, cancellationToken));

    public async Task<PlatformBackupOverviewResponse> GetPlatformOverviewAsync(CancellationToken cancellationToken)
    {
        if (!requestContext.IsPlatformAdministrator)
            throw new DomainException(ErrorKind.Forbidden, "Only platform administrators can do this.");
        return new(await backupStatusRepository.ReadHealthAsync(cancellationToken),
                   await backupStatusRepository.ListBackupRunsAsync(ListLimit, cancellationToken),
                   await backupStatusRepository.ListRestoreDrillsAsync(ListLimit, cancellationToken));
    }
}
