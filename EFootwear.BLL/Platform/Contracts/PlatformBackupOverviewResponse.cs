namespace EFootwear.BLL.Platform.Contracts;

public sealed record PlatformBackupOverviewResponse(
    BackupHealthResponse? Health, IReadOnlyList<BackupRunResponse> Runs, IReadOnlyList<RestoreDrillResponse> Drills);
