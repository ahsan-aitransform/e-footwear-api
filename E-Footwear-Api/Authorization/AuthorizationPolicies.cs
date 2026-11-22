namespace E_Footwear_Api.Authorization;

/// <summary>
/// Names of the authorization policies (use them in <c>[Authorize(Policy = ...)]</c>).
///
///   PlatformAdministratorOnly  - only a platform administrator ("super admin"). Every /platform endpoint uses it.
///   CompanyUserOnly            - only a signed-in company user; platform administrators are refused. Every endpoint that
///                                reads or writes company data uses it, together with a [RequirePermission] code.
/// </summary>
public static class AuthorizationPolicies
{
    public const string PlatformAdministratorOnly = "PlatformAdministratorOnly";
    public const string CompanyUserOnly = "CompanyUserOnly";
}
