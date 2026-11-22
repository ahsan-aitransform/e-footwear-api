using System.Text.Json;
using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Contracts;
using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Security.Repositories;

/// <summary>
/// Writes events the row-change trigger cannot describe (a whole permission set changed, a session ended ...) to audit.activity_log,
/// as the signed-in user, in the same transaction as the change. The log is append-only: the database refuses UPDATE and DELETE.
/// </summary>
public sealed class ActivityLogWriter(DatabaseSession databaseSession) : IActivityLogWriter
{
    public async Task WriteAsync(ActivityLogEntryRecord entry, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(entry.CompanyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var oldValuesJson = entry.OldValues is null ? null : JsonSerializer.Serialize(entry.OldValues);
        var newValuesJson = entry.NewValues is null ? null : JsonSerializer.Serialize(entry.NewValues);

        await databaseContext.Database.ExecuteSqlAsync($"""
            insert into audit.activity_log (company_id, user_id, action, entity_type, entity_id, old_values, new_values, remarks)
            values ({entry.CompanyId}, core.current_user_id(), {entry.Action}, {entry.EntityType}, {entry.EntityId},
                    {oldValuesJson}::jsonb, {newValuesJson}::jsonb, {entry.Remarks})
            """, cancellationToken);
    }
}
