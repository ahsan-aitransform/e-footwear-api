using E_Footwear_Api.Errors;
using EFootwear.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace E_Footwear_Api.Authorization;

/// <summary>
/// Gives the 401 (not signed in) and 403 (policy refused) answers of the authorization middleware the same JSON error
/// format and <c>errorCode</c> as every other error of the API. Everything else passes through unchanged.
/// </summary>
public sealed class ApiAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler defaultHandler = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
                                  PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            await ProblemDetailsWriter.WriteAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized",
                "Sign in first.", ErrorCodes.Unauthorized, context.RequestAborted);
            return;
        }

        if (authorizeResult.Forbidden)
        {
            await ProblemDetailsWriter.WriteAsync(context, StatusCodes.Status403Forbidden, "Forbidden",
                "You are not allowed to use this part of the system.", ErrorCodes.Forbidden, context.RequestAborted);
            return;
        }

        await defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
