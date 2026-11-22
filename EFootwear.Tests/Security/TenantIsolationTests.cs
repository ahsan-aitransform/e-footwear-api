using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Security;
using EFootwear.Tests.Infrastructure;
using Npgsql;

namespace EFootwear.Tests.Security;

/// <summary>
/// Part 5.3 / 31: a company must never reach another company's records by changing an id, and a store user must stay inside
/// their stores. Checked through the API and directly in the database (row-level security).
/// </summary>
[Collection(ApiIntegrationCollection.Name)]
public class TenantIsolationTests(ApiTestEnvironment environment)
{
    private static readonly string UsersUrl = TestScenarioBuilder.Url(ApiRoutes.Users.Root);

    [Fact]
    public async Task Company_owner_cannot_list_or_create_users_of_another_company_by_naming_its_id()
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();
        var companyA = await environment.Scenarios.CreateCompanyAsync(platformClient);
        var companyB = await environment.Scenarios.CreateCompanyAsync(platformClient);
        var ownerAClient = await environment.Scenarios.SignInAsync(companyA.OwnerEmail, companyA.OwnerTemporaryPassword);

        var list = await ownerAClient.GetAsync($"{UsersUrl}?companyId={companyB.CompanyId}");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        var create = await ownerAClient.PostAsJsonAsync(UsersUrl, new
        {
            companyId = companyB.CompanyId,
            email = "intruder@example.test",
            fullName = "Intruder",
            roleCode = RoleCodes.CashierSalesman,
            storeIds = new[] { companyB.FirstStoreId },
            password = TestScenarioBuilder.TemporaryPassword,
        });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        var options = await ownerAClient.GetAsync($"{UsersUrl}/{ApiRoutes.Users.CreationOptions}?companyId={companyB.CompanyId}");
        Assert.Equal(HttpStatusCode.Forbidden, options.StatusCode);
    }

    [Fact]
    public async Task Company_owner_sees_only_their_own_users_and_stores()
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();
        var companyA = await environment.Scenarios.CreateCompanyAsync(platformClient);
        var companyB = await environment.Scenarios.CreateCompanyAsync(platformClient);
        var ownerAClient = await environment.Scenarios.SignInAsync(companyA.OwnerEmail, companyA.OwnerTemporaryPassword);

        var users = await (await ownerAClient.GetAsync(UsersUrl)).Content.ReadFromJsonAsync<JsonElement>();
        var emails = users.EnumerateArray().Select(user => user.GetProperty("email").GetString()).ToList();
        Assert.Contains(companyA.OwnerEmail, emails);
        Assert.DoesNotContain(companyB.OwnerEmail, emails);

        var stores = await (await ownerAClient.GetAsync(TestScenarioBuilder.Url(ApiRoutes.Stores.Root))).Content.ReadFromJsonAsync<JsonElement>();
        var storeIds = stores.EnumerateArray().Select(store => store.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(companyA.FirstStoreId, storeIds);
        Assert.DoesNotContain(companyB.FirstStoreId, storeIds);
    }

    [Fact]
    public async Task Owner_cannot_put_a_user_into_a_store_of_another_company()
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();
        var companyA = await environment.Scenarios.CreateCompanyAsync(platformClient);
        var companyB = await environment.Scenarios.CreateCompanyAsync(platformClient);
        var ownerAClient = await environment.Scenarios.SignInAsync(companyA.OwnerEmail, companyA.OwnerTemporaryPassword);

        var response = await ownerAClient.PostAsJsonAsync(UsersUrl, new
        {
            email = $"stray.{Guid.NewGuid():N}@example.test",
            fullName = "Stray",
            roleCode = RoleCodes.CashierSalesman,
            storeIds = new[] { companyB.FirstStoreId },
            password = TestScenarioBuilder.TemporaryPassword,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Row_level_security_hides_other_companies_even_for_direct_sql()
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();
        var companyA = await environment.Scenarios.CreateCompanyAsync(platformClient);
        var companyB = await environment.Scenarios.CreateCompanyAsync(platformClient);

        await using var connection = new NpgsqlConnection(environment.Database.ApplicationLoginConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        // nobody signed in: no company data at all
        Assert.Equal(0L, await CountAsync(connection, transaction, "select count(*) from sec.users"));

        await SetScopeAsync(connection, transaction, companyA.CompanyId, "on", "");
        Assert.Equal(1L, await CountAsync(connection, transaction, $"select count(*) from sec.users where company_id = '{companyA.CompanyId}'"));
        Assert.Equal(0L, await CountAsync(connection, transaction, $"select count(*) from sec.users where company_id = '{companyB.CompanyId}'"));
        Assert.Equal(0L, await CountAsync(connection, transaction, $"select count(*) from core.stores where company_id = '{companyB.CompanyId}'"));

        // writing into another company is refused by the database itself
        var insertIntoOtherCompany = new NpgsqlCommand(
            $"insert into core.stores (company_id, code, name) values ('{companyB.CompanyId}', 'HACK', 'Hack')", connection, transaction);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => insertIntoOtherCompany.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task Store_manager_sees_and_manages_only_their_own_store()
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();
        var company = await environment.Scenarios.CreateCompanyAsync(platformClient);
        var secondStoreId = await environment.Scenarios.CreateStoreAsync(platformClient, company.CompanyId, "S2");
        var ownerClient = await environment.Scenarios.SignInAsync(company.OwnerEmail, company.OwnerTemporaryPassword);

        var manager = await environment.Scenarios.CreateUserAsync(platformClient, company.CompanyId, RoleCodes.StoreManager, [company.FirstStoreId]);
        var cashierInStore1 = await environment.Scenarios.CreateUserAsync(ownerClient, null, RoleCodes.CashierSalesman, [company.FirstStoreId]);
        var cashierInStore2 = await environment.Scenarios.CreateUserAsync(ownerClient, null, RoleCodes.CashierSalesman, [secondStoreId]);
        var managerClient = await environment.Scenarios.SignInAsync(manager.Email, manager.TemporaryPassword);

        var stores = await (await managerClient.GetAsync(TestScenarioBuilder.Url(ApiRoutes.Stores.Root))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal([company.FirstStoreId], stores.EnumerateArray().Select(store => store.GetProperty("id").GetGuid()).ToArray());

        var users = await (await managerClient.GetAsync(UsersUrl)).Content.ReadFromJsonAsync<JsonElement>();
        var emails = users.EnumerateArray().Select(user => user.GetProperty("email").GetString()).ToList();
        Assert.Contains(cashierInStore1.Email, emails);
        Assert.DoesNotContain(cashierInStore2.Email, emails);
        Assert.DoesNotContain(company.OwnerEmail, emails);

        var createInOtherStore = await managerClient.PostAsJsonAsync(UsersUrl, new
        {
            email = $"other.{Guid.NewGuid():N}@example.test",
            fullName = "Other",
            roleCode = RoleCodes.CashierSalesman,
            storeIds = new[] { secondStoreId },
            password = TestScenarioBuilder.TemporaryPassword,
        });
        Assert.Equal(HttpStatusCode.Forbidden, createInOtherStore.StatusCode);
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task SetScopeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid companyId,
                                            string allStores, string storeIds)
    {
        await using var command = new NpgsqlCommand(
            "select set_config('app.company_id', $1, true), set_config('app.all_stores', $2, true), set_config('app.store_ids', $3, true)",
            connection, transaction)
        {
            Parameters = { new() { Value = companyId.ToString() }, new() { Value = allStores }, new() { Value = storeIds } },
        };
        await command.ExecuteNonQueryAsync();
    }
}
