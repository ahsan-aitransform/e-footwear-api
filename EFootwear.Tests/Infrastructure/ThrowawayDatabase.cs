using EFootwear.DAL.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace EFootwear.Tests.Infrastructure;

/// <summary>
/// A uniquely named PostgreSQL database that exists only for one test run: created from the baseline schema, optionally
/// migrated, dropped on dispose. Several agents (or test classes) can run at the same time without touching each other
/// or the developer's own database.
/// </summary>
public sealed class ThrowawayDatabase : IAsyncDisposable
{
    private const int BaselineCommandTimeoutSeconds = 180;

    private ThrowawayDatabase(string databaseName, string ownerConnectionString, string applicationLoginConnectionString)
    {
        DatabaseName = databaseName;
        OwnerConnectionString = ownerConnectionString;
        ApplicationLoginConnectionString = applicationLoginConnectionString;
        OwnerDataSource = NpgsqlDataSource.Create(ownerConnectionString);
    }

    public string DatabaseName { get; }

    /// <summary>Connects as the database owner (a superuser): bypasses row-level security. For setup and checks only.</summary>
    public string OwnerConnectionString { get; }

    /// <summary>Connects as the test login role: the same kind of connection the API uses.</summary>
    public string ApplicationLoginConnectionString { get; }

    public NpgsqlDataSource OwnerDataSource { get; }

    public static async Task<ThrowawayDatabase> CreateAsync(bool applyMigrations)
    {
        var serverConnectionString = TestDatabaseSettings.GetOwnerConnectionString();
        var databaseName = TestDatabaseSettings.DatabaseNamePrefix + Guid.NewGuid().ToString("N")[..12];

        var maintenance = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = TestDatabaseSettings.MaintenanceDatabaseName };
        await using (var connection = new NpgsqlConnection(maintenance.ConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, $"create database \"{databaseName}\"");
            await EnsureApplicationLoginRoleAsync(connection);
        }

        var ownerConnectionString = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = databaseName }.ConnectionString;
        var applicationLoginConnectionString = new NpgsqlConnectionStringBuilder(serverConnectionString)
        {
            Database = databaseName,
            Username = TestDatabaseSettings.ApplicationLoginRoleName,
            Password = TestDatabaseSettings.ApplicationLoginRolePassword,
        }.ConnectionString;

        var database = new ThrowawayDatabase(databaseName, ownerConnectionString, applicationLoginConnectionString);
        try
        {
            await database.InstallBaselineAsync();
            await database.GrantApplicationRoleToTestLoginAsync();
            if (applyMigrations)
                await database.ApplyMigrationsAsync();
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
        return database;
    }

    public async Task ApplyMigrationsAsync() =>
        await new DatabaseMigrationRunner(OwnerConnectionString, NullLogger<DatabaseMigrationRunner>.Instance)
            .ApplyPendingMigrationsAsync(RepositoryPaths.MigrationsDirectory, CancellationToken.None);

    /// <summary>Runs one statement as the owner, with positional parameters ($1, $2, ...).</summary>
    public async Task<int> ExecuteAsOwnerAsync(string sql, params object?[] parameters)
    {
        await using var command = OwnerDataSource.CreateCommand(sql);
        foreach (var value in parameters) command.Parameters.Add(new NpgsqlParameter { Value = value ?? DBNull.Value });
        return await command.ExecuteNonQueryAsync();
    }

    public async Task<T?> QueryScalarAsOwnerAsync<T>(string sql, params object?[] parameters)
    {
        await using var command = OwnerDataSource.CreateCommand(sql);
        foreach (var value in parameters) command.Parameters.Add(new NpgsqlParameter { Value = value ?? DBNull.Value });
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    private async Task InstallBaselineAsync()
    {
        await using var connection = await OwnerDataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(RepositoryPaths.BaselineSchemaFile), connection)
        {
            CommandTimeout = BaselineCommandTimeoutSeconds,
        };
        await command.ExecuteNonQueryAsync();
    }

    // db/create_app_login.sql uses psql meta commands, so the tests do the same work with plain SQL, under their own role name
    // so the developer's real application login (and its password) is never touched.
    private static async Task EnsureApplicationLoginRoleAsync(NpgsqlConnection connection)
    {
        try
        {
            await ExecuteAsync(connection,
                $"""
                do $$
                begin
                    if not exists (select 1 from pg_roles where rolname = '{TestDatabaseSettings.ApplicationLoginRoleName}') then
                        create role {TestDatabaseSettings.ApplicationLoginRoleName} login nobypassrls nosuperuser nocreatedb nocreaterole
                            password '{TestDatabaseSettings.ApplicationLoginRolePassword}';
                    end if;
                end $$
                """);
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.DuplicateObject or PostgresErrorCodes.UniqueViolation)
        {
            // another test run created it at the same moment
        }
    }

    /// <summary>Grants the test login role the application role. Called after the baseline has created pos_app.</summary>
    public async Task GrantApplicationRoleToTestLoginAsync() =>
        await ExecuteAsOwnerAsync($"grant pos_app to {TestDatabaseSettings.ApplicationLoginRoleName}");

    public async ValueTask DisposeAsync()
    {
        await OwnerDataSource.DisposeAsync();
        NpgsqlConnection.ClearAllPools();

        var serverConnectionString = TestDatabaseSettings.GetOwnerConnectionString();
        var maintenance = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = TestDatabaseSettings.MaintenanceDatabaseName };
        await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, $"drop database if exists \"{DatabaseName}\" with (force)");
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
