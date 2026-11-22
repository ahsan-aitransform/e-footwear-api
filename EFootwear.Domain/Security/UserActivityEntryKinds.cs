namespace EFootwear.Domain.Security;

/// <summary>The two sources of the Users Activity feed (view audit.v_user_activity_feed): data changes and sign-in events.</summary>
public static class UserActivityEntryKinds
{
    public const string BusinessChange = "BUSINESS_CHANGE";
    public const string SignIn = "SIGN_IN";
}
