using EFootwear.BLL.Common;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Middleware;

/// <summary>
/// While a company user still has a temporary password (set when the platform or an owner created the account),
/// only the /auth endpoints work: sign-out, "who am I" and changing the password. Everything else is refused.
/// Runs after authentication, so RequestContext is already filled.
/// </summary>
public sealed class MustChangePasswordGuard(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, RequestContext me)
    {
        var path = http.Request.Path;
        var allowed = path.StartsWithSegments("/api/v1/auth", StringComparison.OrdinalIgnoreCase);

        if (me.IsAuthenticated && !me.IsPlatformAdmin && me.MustChangePassword && !allowed)
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            await http.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Password change required",
                Detail = "You must change your temporary password before using the system.",
                Instance = path,
            });
            return;
        }
        await next(http);
    }
}
