using System.Text.RegularExpressions;
using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Common;

/// <summary>
/// Answers "where is this row used?" with the database function core.find_row_usages, which follows every foreign key that points
/// at the row's table. It runs inside the request's transaction, so row-level security limits the answer to the caller's company.
/// </summary>
public sealed partial class RowUsageRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IRowUsageRepository
{
    [GeneratedRegex("^[a-z_]+\\.[a-z_]+$")]
    private static partial Regex QualifiedTableNamePattern();

    public async Task<IReadOnlyList<RowUsageResponse>> FindUsagesAsync(string tableName, Guid rowId, CancellationToken cancellationToken)
    {
        // The table name comes from code (a MasterListDefinition or a constant), never from a client; the pattern is a second guard.
        if (!QualifiedTableNamePattern().IsMatch(tableName))
            throw new ArgumentException($"'{tableName}' is not a schema-qualified table name.", nameof(tableName));

        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.Database
            .SqlQuery<RowUsageRow>($"""
                select referencing_table as "TableName", usage_count as "UsageCount"
                from core.find_row_usages({companyId}, {tableName}::regclass, {rowId})
                order by referencing_table
                """)
            .Select(row => new RowUsageResponse(row.TableName, row.UsageCount))
            .ToListAsync(cancellationToken);
    }

    private sealed class RowUsageRow
    {
        public string TableName { get; set; } = "";
        public long UsageCount { get; set; }
    }
}
