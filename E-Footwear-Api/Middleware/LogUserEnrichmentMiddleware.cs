using EFootwear.BLL.Common;
using Serilog;
using Serilog.Context;

namespace E_Footwear_Api.Middleware;

/// <summary>
/// After authentication: adds who is calling (company, user or platform administrator) to every log line of the rest
/// of the request, and to the one-line request summary. Ids only: no names, emails or tokens.
/// </summary>
public sealed class LogUserEnrichmentMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, RequestContext me, IDiagnosticContext diagnostics)
    {
        if (!me.IsAuthenticated)
        {
            await next(http);
            return;
        }

        if (me.IsPlatformAdmin)
        {
            diagnostics.Set("PlatformAdminId", me.PlatformAdminId);
            using (LogContext.PushProperty("PlatformAdminId", me.PlatformAdminId))
                await next(http);
            return;
        }

        diagnostics.Set("CompanyId", me.CompanyId);
        diagnostics.Set("UserId", me.UserId);
        using (LogContext.PushProperty("CompanyId", me.CompanyId))
        using (LogContext.PushProperty("UserId", me.UserId))
            await next(http);
    }
}
