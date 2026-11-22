namespace EFootwear.Domain.Security;

/// <summary>One button of a user's quick-action toolbar. Table: sec.user_quick_actions. See <see cref="QuickActionCodes"/>.</summary>
public sealed class UserQuickAction
{
    public Guid CompanyId { get; set; }
    public Guid UserId { get; set; }
    public string ActionCode { get; set; } = "";
    public int SortOrder { get; set; }
}
