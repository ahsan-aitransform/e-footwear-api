namespace EFootwear.BLL.Platform.Contracts;

/// <summary>The plan code never changes. A plan is retired (IsActive = false) instead of deleted; retired plans cannot be assigned.</summary>
public sealed record UpdatePlanRequest(string Name, int MaximumStores, int MaximumUsers, bool IsActive);
