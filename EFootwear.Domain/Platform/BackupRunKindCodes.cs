namespace EFootwear.Domain.Platform;

/// <summary>ops.backup_runs.kind: a base backup of the whole database, the WAL archive, or a logical export of one company.</summary>
public static class BackupRunKindCodes
{
    public const string BaseBackup = "BASE";
    public const string WalArchive = "WAL_ARCHIVE";
    public const string LogicalExport = "LOGICAL_EXPORT";
}
