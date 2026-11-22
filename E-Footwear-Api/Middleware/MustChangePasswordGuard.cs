using E_Footwear_Api.Errors;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.Domain.Common;

namespace E_Footwear_Api.Middleware;

/// <summary>
/// While a company user still has a temporary password (set when the platform or an owner created the account),
/// only the /auth endpoints work: sign-out, "who am I" and changing the password. Everything else is refused.
/// Runs after authentication, so RequestContext is already filled.
/// </summary>
public sealed class MustChangePasswordGuard(RequestDelegate next)
{
    private static readonly string AllowedPathPrefix = "/" + ApiRoutes.Authentication.Root;

    public async Task InvokeAsync(HttpContext httpContext, RequestContext requestContext)
    {
        var isAllowedPath = httpContext.Request.Path.StartsWithSegments(AllowedPathPrefix, StringComparison.OrdinalIgnoreCase);

        if (requestContext.IsAuthenticated && !requestContext.IsPlatformAdministrator
            && requestContext.MustChangePassword && !isAllowedPath)
        {
            await ProblemDetailsWriter.WriteAsync(httpContext, StatusCodes.Status403Forbidden, "Password change required",
                "You must change your temporary password before using the system.", ErrorCodes.PasswordChangeRequired,
                httpContext.RequestAborted);
            return;
        }
        await next(httpContext);
    }
}
