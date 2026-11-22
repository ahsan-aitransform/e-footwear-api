namespace E_Footwear_Api.Authentication;

/// <summary>Names used by the session authentication handler.</summary>
public static class SessionAuthenticationDefaults
{
    public const string Scheme = "Session";

    /// <summary>The claim that holds the company id of a company user. Platform administrators do not have it.</summary>
    public const string CompanyIdClaimType = "company_id";
}
