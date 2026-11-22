namespace EFootwear.BLL.Platform.Contracts;

/// <summary>A backup run as the platform administrator sees it (including the off-site location, which a company never sees).</summary>
public sealed record BackupRunResponse(
    Guid Id,
    string Kind,
    Guid? CompanyId,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string Status,
    string StorageLocation,
    long? SizeBytes,
    bool IsEncrypted,
    string? ErrorMessage);
