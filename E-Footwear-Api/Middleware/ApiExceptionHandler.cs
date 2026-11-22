using E_Footwear_Api.Errors;
using EFootwear.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace E_Footwear_Api.Middleware;

/// <summary>One error format for the whole API (RFC 7807 problem details). Internal details are logged, not returned.</summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    private sealed record MappedError(int StatusCode, string Title, string? Detail, string ErrorCode);

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var error = Map(exception);

        // Expected refusals (validation, wrong password, forbidden...) are normal traffic: one short line, no stack trace.
        // Only genuine faults (5xx) are logged as errors with the full exception.
        if (error.StatusCode >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        else
        {
            var cause = exception is DbUpdateException { InnerException: { } inner } ? inner : exception;
            var level = error.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden or StatusCodes.Status423Locked
                ? LogLevel.Warning
                : LogLevel.Information;
            logger.Log(level, "Request refused with {Status} {Title} on {Method} {Path}: {Reason}",
                error.StatusCode, error.Title, httpContext.Request.Method, httpContext.Request.Path,
                cause is PostgresException postgresException
                    ? $"database rule {postgresException.SqlState} {postgresException.ConstraintName}".Trim()
                    : error.Detail);
        }

        await ProblemDetailsWriter.WriteAsync(httpContext, error.StatusCode, error.Title, error.Detail, error.ErrorCode, cancellationToken);
        return true;
    }

    // EF Core wraps database errors raised while saving; unwrap them so the rules below still match.
    private static MappedError Map(Exception exception) =>
        exception is DbUpdateException { InnerException: PostgresException inner } and not DbUpdateConcurrencyException
            ? Map(inner)
            : MapCore(exception);

    private static MappedError MapCore(Exception exception) => exception switch
    {
        DomainException domainException => domainException.Kind switch
        {
            ErrorKind.Validation => new(StatusCodes.Status400BadRequest, "Validation failed", domainException.Message, domainException.ErrorCode),
            ErrorKind.Unauthorized => new(StatusCodes.Status401Unauthorized, "Unauthorized", domainException.Message, domainException.ErrorCode),
            ErrorKind.Forbidden => new(StatusCodes.Status403Forbidden, "Forbidden", domainException.Message, domainException.ErrorCode),
            ErrorKind.NotFound => new(StatusCodes.Status404NotFound, "Not found", domainException.Message, domainException.ErrorCode),
            ErrorKind.Conflict => new(StatusCodes.Status409Conflict, "Conflict", domainException.Message, domainException.ErrorCode),
            ErrorKind.Locked => new(StatusCodes.Status423Locked, "Locked", domainException.Message, domainException.ErrorCode),
            _ => new(StatusCodes.Status400BadRequest, "Bad request", domainException.Message, domainException.ErrorCode),
        },
        // Someone else saved the same record after this copy was read (row_ver changed).
        DbUpdateConcurrencyException => new(StatusCodes.Status409Conflict, "Conflict",
            "The record was changed by someone else. Reload it and try again.", ErrorCodes.ConcurrentUpdate),
        // Rules enforced by the database surface here when the application layer missed them.
        PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege } =>
            new(StatusCodes.Status403Forbidden, "Forbidden", "Not allowed.", ErrorCodes.DatabasePermissionDenied),
        PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } =>
            new(StatusCodes.Status409Conflict, "Conflict", "A record with the same key already exists.", ErrorCodes.DatabaseUniqueViolation),
        PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } =>
            new(StatusCodes.Status409Conflict, "Conflict", "The record is referenced by, or refers to, a record that does not exist.", ErrorCodes.DatabaseForeignKeyViolation),
        PostgresException { SqlState: PostgresErrorCodes.CheckViolation } checkViolation =>
            new(StatusCodes.Status422UnprocessableEntity, "Rule violation", checkViolation.MessageText, ErrorCodes.DatabaseRuleViolation),
        PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected } =>
            new(StatusCodes.Status409Conflict, "Conflict", "Please retry.", ErrorCodes.Conflict),
        _ => new(StatusCodes.Status500InternalServerError, "Server error", null, ErrorCodes.InternalServerError),
    };
}
