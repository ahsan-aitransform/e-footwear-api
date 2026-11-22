using EFootwear.BLL.Platform.Abstractions;
using EFootwear.BLL.Platform.Contracts;
using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Platform.Repositories;

/// <summary>
/// Reads the backup records through the read-only functions of migration 0150 (ops.* is closed to the application role).
/// ops.list_backup_runs refuses anyone who is not a platform administrator, also at database level.
/// </summary>
public sealed class BackupStatusRepository(DatabaseSession databaseSession) : IBackupStatusRepository
{
    public async Task<BackupHealthResponse?> ReadHealthAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var rows = await databaseContext.Database.SqlQuery<HealthRow>($"""
            select last_base_at as "LastBaseBackupAt", last_wal_at as "LastWalArchiveAt", last_export_at as "LastCompanyExportAt",
                   failed_last_7d::int as "FailedRunsLastSevenDays", last_drill_at as "LastRestoreDrillAt",
                   last_pass_at as "LastPassedRestoreDrillAt", base_status as "BaseBackupStatus",
                   wal_status as "WalArchiveStatus", restore_test_status as "RestoreTestStatus"
              from ops.read_backup_health()
            """).ToListAsync(cancellationToken);
        return rows.Select(row => new BackupHealthResponse(row.LastBaseBackupAt, row.LastWalArchiveAt, row.LastCompanyExportAt,
            row.FailedRunsLastSevenDays, row.LastRestoreDrillAt, row.LastPassedRestoreDrillAt, row.BaseBackupStatus,
            row.WalArchiveStatus, row.RestoreTestStatus)).FirstOrDefault();
    }

    public async Task<IReadOnlyList<BackupRunResponse>> ListBackupRunsAsync(int limit, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var rows = await databaseContext.Database.SqlQuery<RunRow>($"""
            select out_id as "Id", out_kind as "Kind", out_company_id as "CompanyId", out_started_at as "StartedAt",
                   out_finished_at as "FinishedAt", out_status as "Status", out_storage_location as "StorageLocation",
                   out_size_bytes as "SizeBytes", out_encrypted as "IsEncrypted", out_error_message as "ErrorMessage"
              from ops.list_backup_runs({limit})
            """).ToListAsync(cancellationToken);
        return rows.Select(row => new BackupRunResponse(row.Id, row.Kind, row.CompanyId, row.StartedAt, row.FinishedAt, row.Status,
            row.StorageLocation, row.SizeBytes, row.IsEncrypted, row.ErrorMessage)).ToList();
    }

    public async Task<IReadOnlyList<CompanyExportResponse>> ListCompanyExportsAsync(int limit, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var rows = await databaseContext.Database.SqlQuery<ExportRow>($"""
            select out_id as "Id", out_started_at as "StartedAt", out_finished_at as "FinishedAt", out_status as "Status",
                   out_size_bytes as "SizeBytes", out_encrypted as "IsEncrypted"
              from ops.list_company_exports({limit})
            """).ToListAsync(cancellationToken);
        return rows.Select(row => new CompanyExportResponse(row.Id, row.StartedAt, row.FinishedAt, row.Status, row.SizeBytes, row.IsEncrypted)).ToList();
    }

    public async Task<IReadOnlyList<RestoreDrillResponse>> ListRestoreDrillsAsync(int limit, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var rows = await databaseContext.Database.SqlQuery<DrillRow>($"""
            select out_id as "Id", out_started_at as "StartedAt", out_finished_at as "FinishedAt", out_outcome as "Outcome",
                   out_measured_rto_minutes as "MeasuredRtoMinutes", out_measured_rpo_minutes as "MeasuredRpoMinutes",
                   out_rows_checked as "RowsChecked"
              from ops.list_restore_drills({limit})
            """).ToListAsync(cancellationToken);
        return rows.Select(row => new RestoreDrillResponse(row.Id, row.StartedAt, row.FinishedAt, row.Outcome,
            row.MeasuredRtoMinutes, row.MeasuredRpoMinutes, row.RowsChecked)).ToList();
    }

    private sealed class HealthRow
    {
        public DateTimeOffset? LastBaseBackupAt { get; set; }
        public DateTimeOffset? LastWalArchiveAt { get; set; }
        public DateTimeOffset? LastCompanyExportAt { get; set; }
        public int FailedRunsLastSevenDays { get; set; }
        public DateTimeOffset? LastRestoreDrillAt { get; set; }
        public DateTimeOffset? LastPassedRestoreDrillAt { get; set; }
        public string BaseBackupStatus { get; set; } = "";
        public string WalArchiveStatus { get; set; } = "";
        public string RestoreTestStatus { get; set; } = "";
    }

    private sealed class RunRow
    {
        public Guid Id { get; set; }
        public string Kind { get; set; } = "";
        public Guid? CompanyId { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public string Status { get; set; } = "";
        public string StorageLocation { get; set; } = "";
        public long? SizeBytes { get; set; }
        public bool IsEncrypted { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private sealed class ExportRow
    {
        public Guid Id { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public string Status { get; set; } = "";
        public long? SizeBytes { get; set; }
        public bool IsEncrypted { get; set; }
    }

    private sealed class DrillRow
    {
        public Guid Id { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset FinishedAt { get; set; }
        public string Outcome { get; set; } = "";
        public decimal? MeasuredRtoMinutes { get; set; }
        public decimal? MeasuredRpoMinutes { get; set; }
        public long? RowsChecked { get; set; }
    }
}
