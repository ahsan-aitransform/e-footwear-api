using EFootwear.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace EFootwear.Api.Security;

/// <summary>
/// Turns expected failures into RFC 7807 problem responses and never leaks internals for unexpected ones.
/// The v6 database raises user-readable messages from its triggers and functions (ERRCODE P0001 and friends); those are
/// business-rule failures and are passed through as 422 with the database message.
/// </summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> log, IProblemDetailsService problems, IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, code, title, errors) = Map(ex);
        if (status >= 500) log.LogError(ex, "Unhandled exception for {Path}", http.Request.Path);

        http.Response.StatusCode = status;
        var pd = new ProblemDetails { Status = status, Title = title, Type = $"https://efootwear/errors/{code}" };
        pd.Extensions["code"] = code;
        pd.Extensions["traceId"] = http.TraceIdentifier;
        if (errors is not null) pd.Extensions["errors"] = errors;
        // Database detail helps developers; production responses never carry it.
        if (!env.IsProduction() && ex is PostgresException pg) pd.Detail = $"{pg.SqlState}: {pg.MessageText}" + (pg.TableName is null ? "" : $" [{pg.SchemaName}.{pg.TableName}]");
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = pd, Exception = ex });
    }

    private static (int, string, string, object?) Map(Exception ex) => ex switch
    {
        ValidationException v => (400, v.Code, v.Message, v.Errors),
        AppException a => (a.Status, a.Code, a.Message, null),
        PostgresException { SqlState: "23505" } p => (409, "duplicate", Friendly(p, "That record already exists."), null),
        PostgresException { SqlState: "P0002" } p => (404, "not_found", p.MessageText, null),
        PostgresException { SqlState: "23P01" } => (409, "overlap", "This overlaps an existing record (for example a price range that is already defined).", null),
        PostgresException { SqlState: "23503" } => (409, "reference_conflict", "The record refers to, or is used by, other data.", null),
        PostgresException { SqlState: "23514" } p => (422, "check_failed", Friendly(p, "A value is outside the allowed range."), null),
        PostgresException { SqlState: "23502" } p => (400, "required", $"A required value is missing ({p.ColumnName}).", null),
        PostgresException { SqlState: "28000" } => (401, "unauthorized", "Session expired. Please sign in again.", null),
        PostgresException { SqlState: "42501" } => (403, "forbidden", "You do not have permission to do this.", null),
        PostgresException { SqlState: "40001" or "40P01" } => (409, "retry", "The data changed at the same time. Please try again.", null),
        PostgresException { SqlState: "P0001" or "22023" or "55000" or "53400" } p => (422, "business_rule", p.MessageText, null),
        BadHttpRequestException => (400, "bad_request", "The request could not be read.", null),
        OperationCanceledException => (499, "cancelled", "Request cancelled.", null),
        _ => (500, "server_error", "Something went wrong on the server.", null),
    };

    // Constraint messages from the schema's own triggers/checks are written for people; raw constraint text is not.
    private static string Friendly(PostgresException p, string fallback)
        => p.MessageText.Contains("violates", StringComparison.OrdinalIgnoreCase) ? fallback : p.MessageText;
}
