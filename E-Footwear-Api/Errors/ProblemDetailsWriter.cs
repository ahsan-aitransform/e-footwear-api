using E_Footwear_Api.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Errors;

/// <summary>
/// Writes the one error format of the API (RFC 7807 problem details) with two extra fields: <c>errorCode</c> (a value of
/// <c>ErrorCodes</c>, for the frontend to react to) and <c>correlationId</c> (to find the log lines).
/// Everything that answers with an error uses this, so every error looks the same.
/// </summary>
public static class ProblemDetailsWriter
{
    public const string ErrorCodeExtensionName = "errorCode";
    public const string CorrelationIdExtensionName = "correlationId";

    public static async Task WriteAsync(HttpContext httpContext, int statusCode, string title, string? detail,
                                        string errorCode, CancellationToken cancellationToken = default)
    {
        httpContext.Response.StatusCode = statusCode;
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path,
        };
        problem.Extensions[ErrorCodeExtensionName] = errorCode;
        if (httpContext.Items[CorrelationIdMiddleware.ItemKey] is string correlationId)
            problem.Extensions[CorrelationIdExtensionName] = correlationId;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
    }
}
