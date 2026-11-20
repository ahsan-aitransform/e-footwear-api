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
        var correlationId = http.Items[CorrelationIdMiddleware.ItemKey] as string;

        // Expected refusals (validation, wrong password, forbidden...) are normal traffic: one short line, no stack trace.
        // Only genuine faults (5xx) are logged as errors with the full exception.
        if (status >= 500)
            logger.LogError(ex, "Unhandled exception on {Method} {Path}", http.Request.Method, http.Request.Path);
        else
        {
            var cause = ex is Microsoft.EntityFrameworkCore.DbUpdateException { InnerException: { } inner } ? inner : ex;
            var level = status is 401 or 403 or 423 ? LogLevel.Warning : LogLevel.Information;
            logger.Log(level, "Request refused with {Status} {Title} on {Method} {Path}: {Reason}",
                status, title, http.Request.Method, http.Request.Path,
                cause is PostgresException pg ? $"database rule {pg.SqlState} {pg.ConstraintName}".Trim() : detail);
        }

        http.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = http.Request.Path,
        };
        if (correlationId is not null) problem.Extensions["correlationId"] = correlationId;
        await http.Response.WriteAsJsonAsync(problem, ct);
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
