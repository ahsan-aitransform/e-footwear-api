using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Infrastructure;

/// <summary>Business/validation error that maps to a consistent RFC 7807 response.</summary>
public sealed class ApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public static ApiException BadRequest(string code, string msg) => new(400, code, msg);
    public static ApiException Unauthorized(string msg = "Invalid credentials.") => new(401, "unauthorized", msg);
    public static ApiException Forbidden(string msg = "You do not have permission.") => new(403, "forbidden", msg);
    public static ApiException NotFound(string msg = "Not found.") => new(404, "not_found", msg);
    public static ApiException Conflict(string code, string msg) => new(409, code, msg);
}

public sealed class ErrorMiddleware(RequestDelegate next, ILogger<ErrorMiddleware> log)
{
    public async Task Invoke(HttpContext ctx)
    {
        try { await next(ctx); }
        catch (ApiException ex) { await Write(ctx, ex.Status, ex.Code, ex.Message); }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
        { await Write(ctx, 409, "duplicate", "A record with the same unique value already exists."); }
        catch (Exception ex)
        {
            log.LogError(ex, "Unhandled error");
            await Write(ctx, 500, "server_error", "Unexpected server error.");
        }
    }

    private static Task Write(HttpContext ctx, int status, string code, string message)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status, Title = code, Detail = message,
            Extensions = { ["code"] = code }
        });
    }
}
