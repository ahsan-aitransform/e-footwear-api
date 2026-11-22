namespace EFootwear.BLL.Security.Contracts;

/// <summary>The codes (<c>QuickActionCodes</c>) of the quick-action toolbar buttons a user sees, in order.</summary>
public sealed record UserQuickActionsResponse(IReadOnlyList<string> ActionCodes);
