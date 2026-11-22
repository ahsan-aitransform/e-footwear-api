namespace EFootwear.BLL.Platform.Contracts;

/// <summary>Suspends, closes or reactivates a company. A suspended or closed company cannot sign in and its sessions end at once.</summary>
public sealed record SetCompanyStatusRequest(string Status, string? Reason);
