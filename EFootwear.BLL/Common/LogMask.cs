namespace EFootwear.BLL.Common;

/// <summary>
/// Helpers that keep personal data out of the logs. Logs are for finding problems, not for storing emails.
/// Passwords, password hashes and session tokens are never logged anywhere in the code base.
/// </summary>
public static class LogMask
{
    /// <summary>"bilal.owner@shop.pk" becomes "b***@shop.pk": enough to tell accounts apart, not enough to contact them.</summary>
    public static string Email(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "(none)";
        var at = email.IndexOf('@');
        return at <= 0 ? "***" : $"{email[0]}***{email[at..]}";
    }
}
