using System.Security.Claims;
using System.Text.Encodings.Web;
using E_Footwear_Api.Authorization;
using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Platform.Abstractions;
using EFootwear.BLL.Platform.Services;
using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Services;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace E_Footwear_Api.Authentication;

/// <summary>
/// Reads "Authorization: Bearer ..." and fills the request identity. Two kinds of token:
///   {companyId}.{secret}   company user, validated by sec.validate_session (company scope)
///   admin.{secret}         platform administrator, validated by platform.admin_session (no company)
/// Company, store access and permissions come from the database, never from the request.
/// </summary>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder urlEncoder,
    IUserAuthenticationRepository authenticationRepository,
    IPlatformAdministratorRepository platformAdministratorRepository,
    IUnitOfWork unitOfWork,
    RequestContext requestContext)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, urlEncoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorizationHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authorizationHeader)) return AuthenticateResult.NoResult();
        if (!authorizationHeader.StartsWith(HttpAuthorizationScheme.BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.Fail("Unsupported authorization scheme.");

        var rawToken = authorizationHeader[HttpAuthorizationScheme.BearerPrefix.Length..].Trim();
        try
        {
            if (PlatformAdministratorAuthenticationService.TryGetPlatformSecret(rawToken, out var administratorSecret))
                return await AuthenticatePlatformAdministratorAsync(administratorSecret);

            if (!UserAuthenticationService.TryParseToken(rawToken, out var companyId, out var secret))
            {
                Logger.LogWarning("Request rejected: malformed bearer token (from {ClientIpAddress})", Context.Connection.RemoteIpAddress);
                return AuthenticateResult.Fail("Malformed token.");
            }

            return await AuthenticateCompanyUserAsync(companyId, secret);
        }
        catch (DomainException exception) when (exception.Kind is ErrorKind.Unauthorized or ErrorKind.Forbidden)
        {
            Logger.LogWarning("Request rejected: {Reason} (from {ClientIpAddress})", exception.Message, Context.Connection.RemoteIpAddress);
            return AuthenticateResult.Fail(exception.Message);
        }
    }

    private async Task<AuthenticateResult> AuthenticateCompanyUserAsync(Guid companyId, string secret)
    {
        authenticationRepository.UseCompany(companyId);
        var identity = await authenticationRepository.ValidateSessionAsync(companyId, secret, Context.RequestAborted);
        await unitOfWork.CommitAsync(Context.RequestAborted);   // saves the slid idle timeout

        requestContext.SetCompanyUser(companyId, identity);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identity.UserId.ToString()),
            new(ClaimTypes.Role, identity.RoleCode),
            new(SessionAuthenticationDefaults.CompanyIdClaimType, companyId.ToString()),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SessionAuthenticationDefaults.Scheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SessionAuthenticationDefaults.Scheme));
    }

    private async Task<AuthenticateResult> AuthenticatePlatformAdministratorAsync(string secret)
    {
        var administrator = await platformAdministratorRepository.ValidateSessionAsync(secret, Context.RequestAborted);
        requestContext.SetPlatformAdministrator(administrator);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, administrator.AdministratorId.ToString()),
            new(ClaimTypes.Name, administrator.Username),
            new(ClaimTypes.Role, RoleCodes.PlatformAdmin),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SessionAuthenticationDefaults.Scheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SessionAuthenticationDefaults.Scheme));
    }
}
