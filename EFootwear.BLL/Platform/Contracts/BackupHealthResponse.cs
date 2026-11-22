namespace EFootwear.BLL.Platform.Contracts;

/// <summary>The state of the backups, from ops.v_backup_health. The thresholds behind the status values are proposals in the schema.</summary>
public sealed record BackupHealthResponse(
    DateTimeOffset? LastBaseBackupAt,
    DateTimeOffset? LastWalArchiveAt,
    DateTimeOffset? LastCompanyExportAt,
    int FailedRunsLastSevenDays,
    DateTimeOffset? LastRestoreDrillAt,
    DateTimeOffset? LastPassedRestoreDrillAt,
    string BaseBackupStatus,
    string WalArchiveStatus,
    string RestoreTestStatus);
