using EFootwear.BLL.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace E_Footwear_Api.Authorization;

/// <summary>
/// Server-side permission check, e.g. [RequirePermission("SALE_CREATE")]. Hiding a button is not security:
/// this runs even if someone calls the endpoint directly. The decision is made by sec.require_permission.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(string permission) : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var auth = context.HttpContext.RequestServices.GetRequiredService<IAuthRepository>();
        await auth.RequirePermissionAsync(permission, context.HttpContext.RequestAborted);
        await next();
    }
}
