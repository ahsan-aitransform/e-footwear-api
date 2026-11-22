using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EFootwear.DAL.Persistence;

/// <summary>
/// Logs a warning for every database command slower than the threshold. Only the SQL text (with @parameter
/// placeholders) and the time are logged, never parameter values, so no personal data or secrets reach the log.
/// </summary>
public sealed class SlowQueryInterceptor(ILogger<SlowQueryInterceptor> logger) : DbCommandInterceptor
{
    private static readonly TimeSpan SlowQueryThreshold = TimeSpan.FromMilliseconds(500);
    private const int LoggedSqlMaximumLength = 300;

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default)
    {
        LogWhenSlow(command, eventData);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        int result, CancellationToken cancellationToken = default)
    {
        LogWhenSlow(command, eventData);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        object? result, CancellationToken cancellationToken = default)
    {
        LogWhenSlow(command, eventData);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    private void LogWhenSlow(DbCommand command, CommandExecutedEventData eventData)
    {
        if (eventData.Duration < SlowQueryThreshold) return;
        var sql = command.CommandText.ReplaceLineEndings(" ");
        logger.LogWarning("Slow database command: {ElapsedMilliseconds} ms. SQL: {Sql}",
            (long)eventData.Duration.TotalMilliseconds,
            sql.Length > LoggedSqlMaximumLength ? sql[..LoggedSqlMaximumLength] + "..." : sql);
    }
}
