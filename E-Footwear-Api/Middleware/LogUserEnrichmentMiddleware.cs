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
    private const string PlatformAdministratorIdProperty = "PlatformAdminId";
    private const string CompanyIdProperty = "CompanyId";
    private const string UserIdProperty = "UserId";

    public async Task InvokeAsync(HttpContext httpContext, RequestContext requestContext, IDiagnosticContext diagnosticContext)
    {
        if (!requestContext.IsAuthenticated)
        {
            await next(httpContext);
            return;
        }

        if (requestContext.IsPlatformAdministrator)
        {
            diagnosticContext.Set(PlatformAdministratorIdProperty, requestContext.PlatformAdministratorId);
            using (LogContext.PushProperty(PlatformAdministratorIdProperty, requestContext.PlatformAdministratorId))
                await next(httpContext);
            return;
        }

        diagnosticContext.Set(CompanyIdProperty, requestContext.CompanyId);
        diagnosticContext.Set(UserIdProperty, requestContext.UserId);
        using (LogContext.PushProperty(CompanyIdProperty, requestContext.CompanyId))
        using (LogContext.PushProperty(UserIdProperty, requestContext.UserId))
            await next(httpContext);
    }
}
