using EFootwear.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace E_Footwear_Api.Middleware;

/// <summary>One error format for the whole API (RFC 7807 problem details). Internal details are logged, not returned.</summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, title, detail) = Map(ex);
        if (status >= 500) logger.LogError(ex, "Unhandled exception");

        http.Response.StatusCode = status;
        await http.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = http.Request.Path,
        }, ct);
        return true;
    }

    // EF Core wraps database errors raised while saving; unwrap them so the rules below still match.
    private static (int Status, string Title, string? Detail) Map(Exception ex) =>
        ex is Microsoft.EntityFrameworkCore.DbUpdateException { InnerException: PostgresException inner } ? Map(inner) : MapCore(ex);

    private static (int Status, string Title, string? Detail) MapCore(Exception ex) => ex switch
    {
        DomainException d => d.Kind switch
        {
            ErrorKind.Validation => (400, "Validation failed", d.Message),
            ErrorKind.Unauthorized => (401, "Unauthorized", d.Message),
            ErrorKind.Forbidden => (403, "Forbidden", d.Message),
            ErrorKind.NotFound => (404, "Not found", d.Message),
            ErrorKind.Conflict => (409, "Conflict", d.Message),
            ErrorKind.Locked => (423, "Locked", d.Message),
            _ => (400, "Bad request", d.Message),
        },
        // Rules enforced by the database surface here when the app layer missed them.
        PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege } => (403, "Forbidden", "Not allowed."),
        PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } => (409, "Conflict", "A record with the same key already exists."),
        PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } => (409, "Conflict", "The record is referenced by, or refers to, a record that does not exist."),
        PostgresException { SqlState: PostgresErrorCodes.CheckViolation } e => (422, "Rule violation", e.MessageText),
        PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected } => (409, "Conflict", "Please retry."),
        _ => (500, "Server error", null),
    };
}
