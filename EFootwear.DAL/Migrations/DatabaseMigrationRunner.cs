using Microsoft.Extensions.Logging;
using Npgsql;

namespace EFootwear.DAL.Migrations;

/// <summary>
/// Applies the numbered scripts of db/migrations to the database and records them in ops.schema_migrations.
///
///  * The baseline db/schema_v7_uuid.sql is the starting point (migration 0000) and is installed by hand on an empty
///    database; the runner handles everything after it.
///  * Each script runs inside one transaction that the runner opens. Scripts therefore must NOT contain BEGIN or COMMIT.
///  * Every up script needs a down script (NNNN_name.down.sql), so each step can be rolled back.
///  * A script that was edited after it was applied is refused (checksum mismatch): add a new migration instead.
///  * Needs the owner connection (ConnectionStrings:Admin), never the application login.
///  * An advisory lock stops two runners from migrating the same database at the same time.
/// </summary>
public sealed class DatabaseMigrationRunner(string ownerConnectionString, ILogger<DatabaseMigrationRunner> logger)
{
    public const string TrackingTableName = "ops.schema_migrations";
    private const long AdvisoryLockKey = 7_272_700_001;

    private const string CreateTrackingTableSql = $"""
        create table if not exists {TrackingTableName} (
            version     text primary key,
            name        text not null,
            checksum    text not null,
            applied_at  timestamptz not null default now()
        )
        """;

    /// <summary>Applies every migration that has not been applied yet, in number order. Returns the versions applied.</summary>
    public async Task<IReadOnlyList<string>> ApplyPendingMigrationsAsync(string migrationsDirectory, CancellationToken cancellationToken)
    {
        var scripts = MigrationScriptDiscovery.Discover(migrationsDirectory);
        await using var connection = await OpenLockedConnectionAsync(cancellationToken);
        var applied = (await ReadAppliedAsync(connection, cancellationToken)).ToDictionary(migration => migration.Version);

        foreach (var script in scripts.Where(script => applied.ContainsKey(script.Version)))
            if (applied[script.Version].Checksum != script.Checksum)
                throw new InvalidOperationException(
                    $"Migration {script.Version}_{script.Name} was changed after it was applied (checksum mismatch). " +
                    "Never edit an applied migration; add a new one.");

        var appliedNow = new List<string>();
        foreach (var script in scripts.Where(script => !applied.ContainsKey(script.Version)))
        {
            if (script.DownScriptPath is null)
                throw new InvalidOperationException($"Migration {script.Version}_{script.Name} has no down script ({script.Version}_{script.Name}.down.sql).");

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteAsync(connection, transaction, script.UpScriptText, cancellationToken);
            await using (var record = new NpgsqlCommand(
                $"insert into {TrackingTableName} (version, name, checksum) values ($1, $2, $3)", connection, transaction)
            {
                Parameters = { new() { Value = script.Version }, new() { Value = script.Name }, new() { Value = script.Checksum } },
            })
                await record.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation("Migration {Version}_{Name} applied", script.Version, script.Name);
            appliedNow.Add(script.Version);
        }
        return appliedNow;
    }

    /// <summary>
    /// Rolls back every applied migration whose number is greater than or equal to <paramref name="fromVersion"/>, newest first,
    /// by running the down scripts. Returns the versions reverted.
    /// </summary>
    public async Task<IReadOnlyList<string>> RevertFromVersionAsync(string migrationsDirectory, string fromVersion,
                                                                    CancellationToken cancellationToken)
    {
        var scripts = MigrationScriptDiscovery.Discover(migrationsDirectory).ToDictionary(script => script.Version);
        await using var connection = await OpenLockedConnectionAsync(cancellationToken);
        var toRevert = (await ReadAppliedAsync(connection, cancellationToken))
            .Where(migration => string.CompareOrdinal(migration.Version, fromVersion) >= 0)
            .OrderByDescending(migration => migration.Version)
            .ToList();

        var reverted = new List<string>();
        foreach (var migration in toRevert)
        {
            if (!scripts.TryGetValue(migration.Version, out var script))
                throw new InvalidOperationException($"Migration {migration.Version} is applied but its script is not in {migrationsDirectory}.");

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteAsync(connection, transaction, script.DownScriptText, cancellationToken);
            await using (var forget = new NpgsqlCommand(
                $"delete from {TrackingTableName} where version = $1", connection, transaction)
            { Parameters = { new() { Value = migration.Version } } })
                await forget.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation("Migration {Version}_{Name} reverted", migration.Version, migration.Name);
            reverted.Add(migration.Version);
        }
        return reverted;
    }

    public async Task<IReadOnlyList<AppliedMigration>> ListAppliedMigrationsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenLockedConnectionAsync(cancellationToken);
        return await ReadAppliedAsync(connection, cancellationToken);
    }

    private async Task<NpgsqlConnection> OpenLockedConnectionAsync(CancellationToken cancellationToken)
    {
        // No pooling: the advisory lock belongs to the session and must end when this connection is disposed.
        var connectionString = new NpgsqlConnectionStringBuilder(ownerConnectionString) { Pooling = false }.ConnectionString;
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, null, $"select pg_advisory_lock({AdvisoryLockKey})", cancellationToken);
        await ExecuteAsync(connection, null, CreateTrackingTableSql, cancellationToken);
        return connection;
    }

    private static async Task<IReadOnlyList<AppliedMigration>> ReadAppliedAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"select version, name, checksum, applied_at from {TrackingTableName} order by version", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var migrations = new List<AppliedMigration>();
        while (await reader.ReadAsync(cancellationToken))
            migrations.Add(new AppliedMigration(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3)));
        return migrations;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql,
                                           CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
