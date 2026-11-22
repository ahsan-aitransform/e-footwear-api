using EFootwear.BLL.Common;
using EFootwear.Domain.Common;
using Microsoft.AspNetCore.Mvc.Filters;

namespace E_Footwear_Api.Authorization;

/// <summary>
/// Server-side permission check. Pass a constant of <c>PermissionCodes</c>, never a string:
/// <c>[RequirePermission(PermissionCodes.Products.NewProduct.Add)]</c>. Hiding a button is not security: this runs even if
/// someone calls the endpoint directly. The permissions come from the validated session (loaded once per request).
///
///  * Several attributes on one action must ALL be satisfied. Use this when the action also calls a SQL function that checks a
///    legacy code through sec.require_permission: also require that code, for example
///    <c>[RequirePermission(PermissionCodes.RestrictedOperations.Sales.SaleCreate)]</c>.
///  * A platform administrator holds no permission codes and is refused, unless the action sets
///    <see cref="AllowPlatformAdministrator"/> (only for the few endpoints a platform administrator also uses, such as user creation).
///  * Pair it with <c>[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]</c> on company endpoints.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(string permissionCode) : Attribute, IAsyncActionFilter
{
    public string PermissionCode { get; } = permissionCode;

    /// <summary>When true, a platform administrator passes without holding the code.</summary>
    public bool AllowPlatformAdministrator { get; init; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var requestContext = context.HttpContext.RequestServices.GetRequiredService<RequestContext>();

        if (!requestContext.IsAuthenticated)
            throw new DomainException(ErrorKind.Unauthorized, "Sign in first.");

        if (requestContext.IsPlatformAdministrator)
        {
            if (AllowPlatformAdministrator)
            {
                await next();
                return;
            }
            throw Denied(context, "a platform administrator");
        }

        if (!requestContext.HasPermission(PermissionCode))
            throw Denied(context, $"user {requestContext.UserId}");

        await next();
    }

    private DomainException Denied(ActionExecutingContext context, string who)
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger<RequirePermissionAttribute>();
        logger.LogWarning("Permission denied: {Who} lacks {PermissionCode} for {Method} {Path}",
            who, PermissionCode, context.HttpContext.Request.Method, context.HttpContext.Request.Path);
        return new DomainException(ErrorKind.Forbidden, $"You do not have the permission {PermissionCode}.",
                                   ErrorCodes.PermissionDenied);
    }
}
