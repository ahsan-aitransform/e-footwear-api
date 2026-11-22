using EFootwear.Tests.Infrastructure;
using Npgsql;

namespace EFootwear.Tests.Settings;

/// <summary>
/// Rules that must hold in the DATABASE, whoever writes the SQL (rule A5.18/A5.24): the application role cannot change plans or
/// companies directly, cannot read the backup tables, cannot change another company, and the plan limits and the suspended-company
/// rule are enforced by triggers. These tests talk to PostgreSQL as the non-superuser application role, not through the API.
/// </summary>
[Collection(ApiIntegrationCollection.Name)]
public class SettingsDatabaseGuardTests(ApiTestEnvironment environment)
{
    private const string InsufficientPrivilege = "42501";
    private const string CheckViolation = "23514";

    private async Task<NpgsqlConnection> OpenApplicationConnectionAsync()
    {
        var connection = new NpgsqlConnection(environment.Database.ApplicationLoginConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<T?> ScalarInTransactionAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    private static async Task SetSessionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid? companyId, bool platformAdministrator = false)
    {
        await using var command = new NpgsqlCommand(
            "select set_config('app.company_id', $1, true), set_config('app.platform_admin', $2, true), set_config('app.all_stores', 'on', true)",
            connection, transaction)
        {
            Parameters = { new() { Value = companyId?.ToString() ?? "" }, new() { Value = platformAdministrator ? "on" : "off" } },
        };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> SqlStateOfAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (PostgresException exception) { return exception.SqlState; }
    }

    [Fact]
    public async Task The_application_role_cannot_write_plans_or_companies_directly()
    {
        await using var connection = await OpenApplicationConnectionAsync();

        Assert.Equal(InsufficientPrivilege, await SqlStateOfAsync(async () =>
            await new NpgsqlCommand("insert into platform.plans (code, name, max_stores, max_users) values ('HACK', 'x', 1, 1)", connection).ExecuteNonQueryAsync()));
        Assert.Equal(InsufficientPrivilege, await SqlStateOfAsync(async () =>
            await new NpgsqlCommand("update platform.plans set max_stores = 9999", connection).ExecuteNonQueryAsync()));
        Assert.Equal(InsufficientPrivilege, await SqlStateOfAsync(async () =>
            await new NpgsqlCommand("update platform.companies set status = 'ACTIVE'", connection).ExecuteNonQueryAsync()));
    }

    [Fact]
    public async Task The_backup_tables_are_closed_and_the_run_list_is_for_platform_administrators_only()
    {
        await using var connection = await OpenApplicationConnectionAsync();

        Assert.Equal(InsufficientPrivilege, await SqlStateOfAsync(async () =>
            await new NpgsqlCommand("select * from ops.backup_runs", connection).ExecuteScalarAsync()));

        await using var transaction = await connection.BeginTransactionAsync();
        await SetSessionAsync(connection, transaction, companyId: null, platformAdministrator: false);
        Assert.Equal(InsufficientPrivilege, await SqlStateOfAsync(async () =>
            await new NpgsqlCommand("select count(*) from ops.list_backup_runs(10)", connection, transaction).ExecuteScalarAsync()));
        await transaction.RollbackAsync();

        await using var adminTransaction = await connection.BeginTransactionAsync();
        await SetSessionAsync(connection, adminTransaction, companyId: null, platformAdministrator: true);
        Assert.NotNull(await ScalarInTransactionAsync<long>(connection, adminTransaction, "select count(*) from ops.list_backup_runs(10)"));
    }

    [Fact]
    public async Task A_company_cannot_change_the_profile_of_another_company_or_unlisted_fields()
    {
        var first = await SettingsScenario.CreateAsync(environment);
        var second = await SettingsScenario.CreateAsync(environment);
        await using var connection = await OpenApplicationConnectionAsync();

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await SetSessionAsync(connection, transaction, first.Company.CompanyId);
            Assert.Equal(InsufficientPrivilege, await SqlStateOfAsync(async () =>
                await new NpgsqlCommand($"select platform.update_company_profile('{second.Company.CompanyId}', '{{\"name\":\"Hijacked\"}}'::jsonb, null)",
                    connection, transaction).ExecuteScalarAsync()));
        }

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await SetSessionAsync(connection, transaction, first.Company.CompanyId);
            Assert.Equal(CheckViolation, await SqlStateOfAsync(async () =>
                await new NpgsqlCommand($"select platform.update_company_profile('{first.Company.CompanyId}', '{{\"plan_code\":\"FREE\"}}'::jsonb, null)",
                    connection, transaction).ExecuteScalarAsync()));
        }

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await SetSessionAsync(connection, transaction, first.Company.CompanyId);
            Assert.NotNull(await ScalarInTransactionAsync<long>(connection, transaction,
                $"select platform.update_company_profile('{first.Company.CompanyId}', '{{\"ntn\":\"999\"}}'::jsonb, null)"));
        }
    }

    [Fact]
    public async Task Row_level_security_keeps_settings_inside_the_company()
    {
        var first = await SettingsScenario.CreateAsync(environment);
        var second = await SettingsScenario.CreateAsync(environment);
        await using var connection = await OpenApplicationConnectionAsync();

        await using var transaction = await connection.BeginTransactionAsync();
        await SetSessionAsync(connection, transaction, first.Company.CompanyId);
        Assert.Equal(InsufficientPrivilege, await SqlStateOfAsync(async () =>
            await new NpgsqlCommand(
                $"insert into core.company_settings (company_id, section_code, setting_values) values ('{second.Company.CompanyId}', 'TAX', '{{}}')",
                connection, transaction).ExecuteNonQueryAsync()));
    }

    [Fact]
    public async Task The_plan_store_limit_is_enforced_by_the_database_even_for_direct_inserts()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        var planCode = "LIM" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await environment.Database.ExecuteAsOwnerAsync(
            "insert into platform.plans (code, name, max_stores, max_users) values ($1, 'Limit test', 1, 100)", planCode);
        await environment.Database.ExecuteAsOwnerAsync("update platform.companies set plan_code = $1 where id = $2", planCode, scenario.Company.CompanyId);

        var state = await SqlStateOfAsync(async () => await environment.Database.ExecuteAsOwnerAsync(
            "insert into core.stores (company_id, code, name) values ($1, 'OVER', 'Over the limit')", scenario.Company.CompanyId));

        Assert.Equal(CheckViolation, state);
    }

    [Fact]
    public async Task The_plan_user_limit_is_enforced_by_the_database()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        var planCode = "USR" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await environment.Database.ExecuteAsOwnerAsync(
            "insert into platform.plans (code, name, max_stores, max_users) values ($1, 'User limit test', 10, 1)", planCode);
        await environment.Database.ExecuteAsOwnerAsync("update platform.companies set plan_code = $1 where id = $2", planCode, scenario.Company.CompanyId);

        var state = await SqlStateOfAsync(async () => await environment.Database.ExecuteAsOwnerAsync(
            "insert into sec.users (company_id, username, email, password_hash, full_name, role_code) values ($1, 'x@example.test', 'x@example.test', 'h', 'X', 'CASHIER_SALESMAN')",
            scenario.Company.CompanyId));

        Assert.Equal(CheckViolation, state);
    }

    [Fact]
    public async Task A_suspended_company_cannot_get_a_new_session()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        var ownerId = await environment.Database.QueryScalarAsOwnerAsync<Guid>(
            "select id from sec.users where company_id = $1 limit 1", scenario.Company.CompanyId);
        await environment.Database.ExecuteAsOwnerAsync("update platform.companies set status = 'SUSPENDED' where id = $1", scenario.Company.CompanyId);

        var state = await SqlStateOfAsync(async () => await environment.Database.ExecuteAsOwnerAsync(
            "insert into sec.sessions (company_id, user_id, token_hash, expires_at, idle_expires_at) values ($1, $2, $3, now() + interval '1 hour', now() + interval '10 minutes')",
            scenario.Company.CompanyId, ownerId, Guid.NewGuid().ToString("N")));

        Assert.Equal(InsufficientPrivilege, state);
    }
}
