namespace EFootwear.BLL.Platform.Contracts;

/// <summary>What the Restore screen shows: the restore tests done so far. A restore itself is an operator procedure, never a tenant action.</summary>
public sealed record RestoreOverviewResponse(BackupHealthResponse? Health, IReadOnlyList<RestoreDrillResponse> Drills);
