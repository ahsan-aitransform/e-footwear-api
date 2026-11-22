namespace EFootwear.Domain.Security.Permissions;

/// <summary>The difference between a user's current permission codes and the set that was asked for.</summary>
public sealed record PermissionChange(IReadOnlyList<string> AddedCodes, IReadOnlyList<string> RemovedCodes)
{
    public bool IsEmpty => AddedCodes.Count == 0 && RemovedCodes.Count == 0;
}
