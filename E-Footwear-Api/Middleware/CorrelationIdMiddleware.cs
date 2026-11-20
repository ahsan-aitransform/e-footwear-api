using Serilog.Context;

namespace E_Footwear_Api.Middleware;

/// <summary>
/// Gives every request a correlation id. The client may send its own in "X-Correlation-Id" (so one user action can be
/// traced from the browser to the API); otherwise one is generated. The id is added to every log line of the request,
/// returned in the response header and in error responses, so a user can quote it and we can find the exact log lines.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string Header = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext http)
    {
        var id = http.Request.Headers[Header].ToString();
        // Accept only a short, harmless value; anything else would let a client forge or flood log lines.
        if (id.Length is 0 or > 64 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            id = Guid.NewGuid().ToString("N");

        http.Items[ItemKey] = id;
        http.Response.Headers[Header] = id;
        using (LogContext.PushProperty("CorrelationId", id))
            await next(http);
    }
}
