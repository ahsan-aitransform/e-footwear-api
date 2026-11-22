namespace EFootwear.Domain.Platform;

/// <summary>ops.backup_runs.status. A run is OK only with a size, a checksum and encryption (database CHECK).</summary>
public static class BackupRunStatusCodes
{
    public const string Running = "RUNNING";
    public const string Ok = "OK";
    public const string Failed = "FAILED";
}
