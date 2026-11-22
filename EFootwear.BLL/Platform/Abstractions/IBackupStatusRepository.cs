using EFootwear.BLL.Platform.Contracts;

namespace EFootwear.BLL.Platform.Abstractions;

/// <summary>Read-only access to the backup records. ops.* is closed to the application role; the DAL calls read functions.</summary>
public interface IBackupStatusRepository
{
    Task<BackupHealthResponse?> ReadHealthAsync(CancellationToken cancellationToken);

    /// <summary>Platform administrators only (the database function refuses anyone else).</summary>
    Task<IReadOnlyList<BackupRunResponse>> ListBackupRunsAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Exports of the caller's own company.</summary>
    Task<IReadOnlyList<CompanyExportResponse>> ListCompanyExportsAsync(int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<RestoreDrillResponse>> ListRestoreDrillsAsync(int limit, CancellationToken cancellationToken);
}
