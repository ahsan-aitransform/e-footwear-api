using EFootwear.Domain.Security.Permissions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace EFootwear.DAL.Security.Permissions;

/// <summary>
/// At startup, copies the in-code permission catalog (the single source of truth) into sec.permissions, so the foreign key
/// of sec.user_permissions accepts every catalog code and the SQL functions can see them. It only inserts or updates;
/// it never deletes, because user_permissions rows may still refer to a code. Running it twice changes nothing.
/// Needs migration 0001 (the module/screen/action/sort_order columns); without it the API refuses to start with a
/// clear message instead of failing later on the first permission grant.
/// </summary>
public sealed class PermissionCatalogSynchronizer(NpgsqlDataSource dataSource, ILogger<PermissionCatalogSynchronizer> logger)
    : IHostedService
{
    private const string RequiredColumnName = "module_code";

    private const string UpsertSql = """
        insert into sec.permissions (code, description, module_code, screen_code, action_code, sort_order)
        select code, description, module_code, screen_code, action_code, sort_order
          from unnest($1::text[], $2::text[], $3::text[], $4::text[], $5::text[], $6::int[])
               as catalog(code, description, module_code, screen_code, action_code, sort_order)
        on conflict (code) do update
           set description = excluded.description,
               module_code = excluded.module_code,
               screen_code = excluded.screen_code,
               action_code = excluded.action_code,
               sort_order  = excluded.sort_order
         where (sec.permissions.description, sec.permissions.module_code, sec.permissions.screen_code,
                sec.permissions.action_code, sec.permissions.sort_order)
               is distinct from
               (excluded.description, excluded.module_code, excluded.screen_code, excluded.action_code, excluded.sort_order)
        """;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var changedRowCount = await SynchronizeAsync(dataSource, cancellationToken);
        logger.LogInformation("Permission catalog synchronized: {PermissionCount} permissions in the catalog, {ChangedRowCount} rows inserted or updated.",
            PermissionCatalog.AllEntries.Count, changedRowCount);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Upserts the catalog and returns how many rows were inserted or changed. Public so tests can call it.</summary>
    public static async Task<int> SynchronizeAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        await using (var columnCheck = new NpgsqlCommand(
            "select count(*) from information_schema.columns where table_schema = 'sec' and table_name = 'permissions' and column_name = $1",
            connection) { Parameters = { new() { Value = RequiredColumnName } } })
        {
            if ((long)(await columnCheck.ExecuteScalarAsync(cancellationToken))! == 0)
                throw new InvalidOperationException(
                    "sec.permissions has no module_code column. Apply the database migrations first: " +
                    "dotnet run --project E-Footwear-Api -- migrate");
        }

        var entries = PermissionCatalog.AllEntries;
        await using var upsert = new NpgsqlCommand(UpsertSql, connection)
        {
            Parameters =
            {
                new() { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text, Value = entries.Select(entry => entry.Action.PermissionCode).ToArray() },
                new() { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text, Value = entries.Select(entry => entry.Action.Description).ToArray() },
                new() { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text, Value = entries.Select(entry => entry.ModuleCode).ToArray() },
                new() { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text, Value = entries.Select(entry => entry.ScreenCode).ToArray() },
                new() { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text, Value = entries.Select(entry => entry.Action.ActionCode).ToArray() },
                new() { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer, Value = entries.Select(entry => entry.SortOrder).ToArray() },
            },
        };
        return await upsert.ExecuteNonQueryAsync(cancellationToken);
    }
}
