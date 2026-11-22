using EFootwear.DAL.Migrations;
using EFootwear.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace EFootwear.Tests.Settings;

/// <summary>Migration 0150: applies, rolls back completely (tables, column, functions, grants) and applies again.</summary>
public class SettingsMigrationTests : IAsyncLifetime
{
    private const string MigrationVersion = "0150";

    private ThrowawayDatabase database = null!;
    private DatabaseMigrationRunner runner = null!;

    public async Task InitializeAsync()
    {
        database = await ThrowawayDatabase.CreateAsync(applyMigrations: false);
        runner = new DatabaseMigrationRunner(database.OwnerConnectionString, NullLogger<DatabaseMigrationRunner>.Instance);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private Task<bool> SettingsObjectsExistAsync() => database.QueryScalarAsOwnerAsync<bool>(
        """
        select exists (select 1 from information_schema.tables where table_schema = 'core' and table_name = 'company_settings')
           and exists (select 1 from information_schema.columns where table_schema = 'platform' and table_name = 'plans' and column_name = 'is_active')
           and exists (select 1 from pg_proc p join pg_namespace n on n.oid = p.pronamespace where n.nspname = 'platform' and p.proname = 'update_company_profile')
        """);

    [Fact]
    public async Task The_migration_applies_rolls_back_and_applies_again()
    {
        var applied = await runner.ApplyPendingMigrationsAsync(RepositoryPaths.MigrationsDirectory, CancellationToken.None);
        Assert.Contains(MigrationVersion, applied);
        Assert.True(await SettingsObjectsExistAsync());

        var reverted = await runner.RevertFromVersionAsync(RepositoryPaths.MigrationsDirectory, MigrationVersion, CancellationToken.None);
        Assert.Contains(MigrationVersion, reverted);
        Assert.False(await SettingsObjectsExistAsync());
        Assert.False(await database.QueryScalarAsOwnerAsync<bool>(
            "select exists (select 1 from information_schema.tables where table_schema = 'core' and table_name in ('store_settings', 'user_preferences'))"));
        Assert.True(await database.QueryScalarAsOwnerAsync<bool>("select has_table_privilege('pos_app', 'platform.plans', 'INSERT')"));

        await runner.ApplyPendingMigrationsAsync(RepositoryPaths.MigrationsDirectory, CancellationToken.None);
        Assert.True(await SettingsObjectsExistAsync());
        Assert.False(await database.QueryScalarAsOwnerAsync<bool>("select has_table_privilege('pos_app', 'platform.plans', 'INSERT')"));
    }
}
