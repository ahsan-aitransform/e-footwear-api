namespace EFootwear.BLL.Platform.Contracts;

/// <summary>What a company uses against its plan. Active stores do not count the head office; users do not count deleted users.</summary>
public sealed record PlanUsageResponse(string PlanCode, string PlanName, int MaximumStores, int MaximumUsers, int ActiveStores, int Users);
