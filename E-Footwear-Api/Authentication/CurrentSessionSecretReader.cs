using E_Footwear_Api.Authorization;
using EFootwear.BLL.Security.Services;

namespace E_Footwear_Api.Authentication;

/// <summary>
/// Reads the secret part of the caller's own bearer token ("{companyId}.{secret}"), so the sessions screen can mark which session is
/// in use and keep it when "end all other sessions" is chosen. The secret is only compared with stored hashes; it is never logged or stored.
/// </summary>
public static class CurrentSessionSecretReader
{
    public static string? Read(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith(HttpAuthorizationScheme.BearerPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var token = header[HttpAuthorizationScheme.BearerPrefix.Length..].Trim();
        return UserAuthenticationService.TryParseToken(token, out _, out var secret) ? secret : null;
    }
}
