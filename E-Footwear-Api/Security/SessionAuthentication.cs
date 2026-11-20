using System.Security.Claims;
using System.Text.Encodings.Web;
using EFootwear.Application.Abstractions;
using EFootwear.Application.Auth;
using EFootwear.Domain.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EFootwear.Api.Security;

/// <summary>Scoped holder of the caller's context for the current request.</summary>
public sealed class CurrentUserAccessor : ICurrentUser
{
    private UserContext? _user;
    public bool IsAuthenticated => _user is not null;
    public UserContext User => _user ?? throw new UnauthorizedAppException("Not signed in.");
    public void Set(UserContext user) => _user = user;
}

public static class SessionDefaults
{
    public const string Scheme = "Session";
    public const string StoreHeader = "X-Store-Id";
    public const string DeviceHeader = "X-Device";
}

/// <summary>
/// Authenticates "Authorization: Bearer {companyId}.{secret}". The database validates the session on every request
/// (revocation, idle and absolute expiry, blocked users) and the caller's rights are re-read, so a block or a removed
/// permission takes effect on the next call (Part 6, Part 31).
/// </summary>
public sealed class SessionAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, AuthService auth, CurrentUserAccessor current)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();

        long? store = long.TryParse(Request.Headers[SessionDefaults.StoreHeader], out var s) ? s : null;
        try
        {
            var user = await auth.AuthenticateAsync(header["Bearer ".Length..].Trim(), store, Context.Connection.RemoteIpAddress?.ToString(),
                Request.Headers[SessionDefaults.DeviceHeader].FirstOrDefault(), Context.RequestAborted);
            current.Set(user);
            var id = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()), new Claim(ClaimTypes.Name, user.UserName)], Scheme.Name);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(id), Scheme.Name));
        }
        catch (UnauthorizedAppException e) { return AuthenticateResult.Fail(e.Message); }
        catch (ForbiddenException e) { return AuthenticateResult.Fail(e.Message); }
    }
}

/// <summary>While the account still has a temporary password, only sign-out, "who am I" and password change are allowed.</summary>
public sealed class MustChangePasswordMiddleware(RequestDelegate next)
{
    private static readonly string[] Allowed = ["/api/v1/auth/"];

    public async Task InvokeAsync(HttpContext ctx, CurrentUserAccessor current)
    {
        if (current.IsAuthenticated && current.User.MustChangePassword
            && !Allowed.Any(a => ctx.Request.Path.StartsWithSegments(a.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
            throw new ForbiddenException("You must change your temporary password first.") ;
        await next(ctx);
    }
}
