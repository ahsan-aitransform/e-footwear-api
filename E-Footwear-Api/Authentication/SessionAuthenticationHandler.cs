using System.Security.Claims;
using System.Text.Encodings.Web;
using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Auth;
using EFootwear.BLL.Common;
using EFootwear.BLL.Platform;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace E_Footwear_Api.Authentication;

public static class SessionAuth
{
    public const string Scheme = "Session";
}

/// <summary>
/// Reads "Authorization: Bearer ..." and fills the request identity. Two kinds of token:
///   {companyId}.{secret}   company user, validated by sec.validate_session (company scope)
///   admin.{secret}         platform administrator, validated by platform.admin_session (no company)
/// Company and store access come from the database, never from the request.
/// </summary>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IAuthRepository auth, IPlatformAdminRepository platformAdmins, IUnitOfWork uow, RequestContext context)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header)) return AuthenticateResult.NoResult();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.Fail("Unsupported authorization scheme.");

        var raw = header["Bearer ".Length..].Trim();
        try
        {
            if (PlatformAuthService.TryGetPlatformSecret(raw, out var adminSecret))
                return await AuthenticatePlatformAdminAsync(adminSecret);

            if (!AuthService.TryParseToken(raw, out var companyId, out var secret))
            {
                Logger.LogWarning("Request rejected: malformed bearer token (from {Ip})", Context.Connection.RemoteIpAddress);
                return AuthenticateResult.Fail("Malformed token.");
            }

            return await AuthenticateCompanyUserAsync(companyId, secret);
        }
        catch (DomainException ex) when (ex.Kind is ErrorKind.Unauthorized or ErrorKind.Forbidden)
        {
            Logger.LogWarning("Request rejected: {Reason} (from {Ip})", ex.Message, Context.Connection.RemoteIpAddress);
            return AuthenticateResult.Fail(ex.Message);
        }
    }

    private async Task<AuthenticateResult> AuthenticateCompanyUserAsync(Guid companyId, string secret)
    {
        auth.UseCompany(companyId);
        var identity = await auth.ValidateSessionAsync(companyId, secret, Context.RequestAborted);
        await uow.CommitAsync(Context.RequestAborted);   // saves the slid idle timeout

        context.Set(companyId, identity);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identity.UserId.ToString()),
            new(ClaimTypes.Role, identity.RoleCode),
            new("company_id", companyId.ToString()),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SessionAuth.Scheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SessionAuth.Scheme));
    }

    private async Task<AuthenticateResult> AuthenticatePlatformAdminAsync(string secret)
    {
        var admin = await platformAdmins.ValidateSessionAsync(secret, Context.RequestAborted);
        context.SetPlatformAdmin(admin);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, admin.AdminId.ToString()),
            new(ClaimTypes.Name, admin.Username),
            new(ClaimTypes.Role, RoleCodes.PlatformAdmin),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SessionAuth.Scheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SessionAuth.Scheme));
    }
}
