namespace EFootwear.BLL.Security.Contracts;

public sealed record ReplaceUserQuickActionsRequest(IReadOnlyList<string> ActionCodes);
