using EFootwear.DAL.Migrations;
using EFootwear.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace EFootwear.Tests.Persistence;

/// <summary>Migration runner: apply, apply again (nothing), roll back, re-apply, refuse an edited script, refuse a missing down script.</summary>
public class DatabaseMigrationRunnerTests : IAsyncLifetime
{
    private const string FirstMigrationVersion = "0001";

    private ThrowawayDatabase database = null!;
    private DatabaseMigrationRunner runner = null!;

    public async Task InitializeAsync()
    {
        database = await ThrowawayDatabase.CreateAsync(applyMigrations: false);
        runner = new DatabaseMigrationRunner(database.OwnerConnectionString, NullLogger<DatabaseMigrationRunner>.Instance);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private Task<bool> PermissionColumnExistsAsync() => database.QueryScalarAsOwnerAsync<bool>(
        "select exists (select 1 from information_schema.columns where table_schema = 'sec' and table_name = 'permissions' and column_name = 'module_code')");

    [Fact]
    public async Task Pending_migrations_are_applied_once_and_recorded()
    {
        Assert.False(await PermissionColumnExistsAsync());

        var applied = await runner.ApplyPendingMigrationsAsync(RepositoryPaths.MigrationsDirectory, CancellationToken.None);

        Assert.Contains(FirstMigrationVersion, applied);
        Assert.True(await PermissionColumnExistsAsync());
        var recorded = await runner.ListAppliedMigrationsAsync(CancellationToken.None);
        Assert.Contains(recorded, migration => migration.Version == FirstMigrationVersion && migration.Checksum.Length == 64);

        Assert.Empty(await runner.ApplyPendingMigrationsAsync(RepositoryPaths.MigrationsDirectory, CancellationToken.None));
    }

    [Fact]
    public async Task Down_scripts_roll_back_and_the_migration_can_be_applied_again()
    {
        await runner.ApplyPendingMigrationsAsync(RepositoryPaths.MigrationsDirectory, CancellationToken.None);

        var reverted = await runner.RevertFromVersionAsync(RepositoryPaths.MigrationsDirectory, FirstMigrationVersion, CancellationToken.None);

        Assert.Contains(FirstMigrationVersion, reverted);
        Assert.False(await PermissionColumnExistsAsync());
        Assert.DoesNotContain(await runner.ListAppliedMigrationsAsync(CancellationToken.None), migration => migration.Version == FirstMigrationVersion);

        await runner.ApplyPendingMigrationsAsync(RepositoryPaths.MigrationsDirectory, CancellationToken.None);
        Assert.True(await PermissionColumnExistsAsync());
    }

    [Fact]
    public async Task A_migration_edited_after_it_was_applied_is_refused()
    {
        var copiedDirectory = CopyMigrationsToTemporaryDirectory();
        try
        {
            await runner.ApplyPendingMigrationsAsync(copiedDirectory, CancellationToken.None);
            var upScript = Directory.GetFiles(copiedDirectory, $"{FirstMigrationVersion}_*.sql").Single(path => !path.EndsWith(".down.sql"));
            await File.AppendAllTextAsync(upScript, "\n-- edited after it was applied\n");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                runner.ApplyPendingMigrationsAsync(copiedDirectory, CancellationToken.None));
            Assert.Contains("checksum", exception.Message);
        }
        finally { Directory.Delete(copiedDirectory, recursive: true); }
    }

    [Fact]
    public async Task A_migration_without_a_down_script_is_refused()
    {
        var copiedDirectory = CopyMigrationsToTemporaryDirectory();
        try
        {
            File.Delete(Directory.GetFiles(copiedDirectory, "*.down.sql").First());

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                runner.ApplyPendingMigrationsAsync(copiedDirectory, CancellationToken.None));
            Assert.Contains("down script", exception.Message);
        }
        finally { Directory.Delete(copiedDirectory, recursive: true); }
    }

    [Fact]
    public async Task A_script_with_a_bad_name_is_refused()
    {
        var copiedDirectory = CopyMigrationsToTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(copiedDirectory, "not-a-migration.sql"), "select 1;");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                runner.ApplyPendingMigrationsAsync(copiedDirectory, CancellationToken.None));
        }
        finally { Directory.Delete(copiedDirectory, recursive: true); }
    }

    private static string CopyMigrationsToTemporaryDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("efw-migrations-").FullName;
        foreach (var path in Directory.GetFiles(RepositoryPaths.MigrationsDirectory, "*.sql"))
            File.Copy(path, Path.Combine(directory, Path.GetFileName(path)));
        return directory;
    }
}
