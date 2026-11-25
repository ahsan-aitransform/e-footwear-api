using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;

namespace EFootwear.Tests.Accounting;

/// <summary>GL-Accounts (docx 2.1), New Second Level (2.2) and View Second Level (2.3): the chart of accounts master data.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class ChartOfAccountsApiTests(ApiTestEnvironment environment)
{
    private static readonly string GroupsUrl = TestScenarioBuilder.Url(ApiRoutes.ChartOfAccounts.Root, ApiRoutes.ChartOfAccounts.Groups);
    private static readonly string NextGroupCodeUrl = TestScenarioBuilder.Url(ApiRoutes.ChartOfAccounts.Root, ApiRoutes.ChartOfAccounts.NextGroupCode);
    private static readonly string AccountsUrl = TestScenarioBuilder.Url(ApiRoutes.ChartOfAccounts.Root, ApiRoutes.ChartOfAccounts.Accounts);
    private static readonly string NextAccountCodeUrl = TestScenarioBuilder.Url(ApiRoutes.ChartOfAccounts.Root, ApiRoutes.ChartOfAccounts.NextAccountCode);
    private static readonly string MainTypesUrl = TestScenarioBuilder.Url(ApiRoutes.ChartOfAccounts.Root, ApiRoutes.ChartOfAccounts.MainTypes);

    private static async Task<JsonElement[]> ListGroupsAsync(HttpClient client, string? mainTypeCode = null)
    {
        var url = mainTypeCode is null ? GroupsUrl : $"{GroupsUrl}?mainTypeCode={mainTypeCode}";
        return (await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(url))).EnumerateArray().ToArray();
    }

    private static async Task<Guid> CreateAccountAsync(HttpClient client, string groupCode, string name) =>
        (await OwnerCompanyScenario.PostJsonAsync(client, AccountsUrl, new { groupCode, name })).GetProperty("id").GetGuid();

    [Fact]
    public async Task A_new_company_has_the_default_groups_with_titles()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);

        var groups = await ListGroupsAsync(scenario.OwnerClient);
        var titles = groups.ToDictionary(group => group.GetProperty("groupCode").GetString()!, group => group.GetProperty("name").GetString()!);
        Assert.Equal("Cash", titles["101"]);
        Assert.Equal("Bank", titles["102"]);
        Assert.Equal("Subledger control", titles["103"]);
        Assert.Equal("Opening balance equity", titles["302"]);

        var assets = await ListGroupsAsync(scenario.OwnerClient, "ASSET");
        Assert.Equal(["101", "102", "103"], assets.Select(group => group.GetProperty("groupCode").GetString()).ToArray());

        var mainTypes = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(MainTypesUrl));
        Assert.Equal(6, mainTypes.GetArrayLength());
    }

    [Fact]
    public async Task A_second_level_group_gets_the_next_code_and_needs_a_unique_title()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);

        var preview = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{NextGroupCodeUrl}?mainTypeCode=ASSET"));
        Assert.Equal("104", preview.GetProperty("groupCode").GetString());

        var created = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, GroupsUrl, new { mainTypeCode = "ASSET", name = "Accounts Receivable" });
        Assert.Equal("104", created.GetProperty("groupCode").GetString());

        var next = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, GroupsUrl, new { mainTypeCode = "ASSET", name = "Fixed Assets" });
        Assert.Equal("105", next.GetProperty("groupCode").GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await scenario.OwnerClient.PostAsJsonAsync(GroupsUrl, new { mainTypeCode = "ASSET", name = "accounts receivable" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PostAsJsonAsync(GroupsUrl, new { mainTypeCode = "PROFIT", name = "Whatever" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PostAsJsonAsync(GroupsUrl, new { mainTypeCode = "ASSET", name = " " })).StatusCode);

        // the same title is fine under another main type
        Assert.Equal(HttpStatusCode.Created, (await scenario.OwnerClient.PostAsJsonAsync(GroupsUrl, new { mainTypeCode = "LIABILITY", name = "Fixed Assets" })).StatusCode);
    }

    [Fact]
    public async Task A_group_title_can_be_changed_and_an_unused_group_deleted_but_a_group_with_accounts_cannot()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, GroupsUrl, new { mainTypeCode = "ASSET", name = "Notes Receivable" });
        var group = (await ListGroupsAsync(scenario.OwnerClient, "ASSET")).Single(candidate => candidate.GetProperty("groupCode").GetString() == "104");

        var rename = await scenario.OwnerClient.PutAsJsonAsync($"{GroupsUrl}/104", new { name = "Notes and Cheques", rowVersion = group.GetProperty("rowVersion").GetInt64() });
        Assert.Equal(HttpStatusCode.NoContent, rename.StatusCode);
        var renamed = (await ListGroupsAsync(scenario.OwnerClient, "ASSET")).Single(candidate => candidate.GetProperty("groupCode").GetString() == "104");
        Assert.Equal("Notes and Cheques", renamed.GetProperty("name").GetString());

        // a default group is used by accounts
        var inUse = await scenario.OwnerClient.DeleteAsync($"{GroupsUrl}/101");
        Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
        Assert.Equal(ErrorCodes.RecordInUse, (await OwnerCompanyScenario.ReadJsonAsync(inUse)).GetProperty("errorCode").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await scenario.OwnerClient.DeleteAsync($"{GroupsUrl}/104")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.OwnerClient.DeleteAsync($"{GroupsUrl}/104")).StatusCode);
    }

    [Fact]
    public async Task Account_codes_are_the_group_code_plus_a_five_digit_serial()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);

        var preview = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{NextAccountCodeUrl}?groupCode=102"));
        Assert.Equal("10200001", preview.GetProperty("accountCode").GetString());

        await CreateAccountAsync(scenario.OwnerClient, "102", "Main Store Bank");
        await CreateAccountAsync(scenario.OwnerClient, "102", "ShoeZone Bank");

        var list = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{AccountsUrl}?groupCode=102"));
        var created = list.GetProperty("items").EnumerateArray()
            .Where(account => account.GetProperty("code").GetString()!.StartsWith("102000")).ToArray();
        Assert.Equal(["10200001", "10200002"], created.Select(account => account.GetProperty("code").GetString()).Order().ToArray());
        Assert.All(created, account => Assert.Equal("ASSET", account.GetProperty("mainTypeCode").GetString()));
        Assert.All(created, account => Assert.Equal("Bank", account.GetProperty("groupName").GetString()));
    }

    [Fact]
    public async Task An_account_title_is_unique_inside_its_group_and_the_group_must_exist()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        await CreateAccountAsync(scenario.OwnerClient, "102", "Habib Bank");

        Assert.Equal(HttpStatusCode.Conflict, (await scenario.OwnerClient.PostAsJsonAsync(AccountsUrl, new { groupCode = "102", name = "HABIB BANK" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await scenario.OwnerClient.PostAsJsonAsync(AccountsUrl, new { groupCode = "101", name = "Habib Bank" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.OwnerClient.PostAsJsonAsync(AccountsUrl, new { groupCode = "199", name = "Nowhere" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PostAsJsonAsync(AccountsUrl, new { groupCode = "10", name = "Bad code" })).StatusCode);
    }

    [Fact]
    public async Task An_unused_account_can_be_renamed_deactivated_and_deleted()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var accountId = await CreateAccountAsync(scenario.OwnerClient, "301", "Investment");
        var list = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{AccountsUrl}?searchText=Investment"));
        var rowVersion = list.GetProperty("items").EnumerateArray().Single(account => account.GetProperty("id").GetGuid() == accountId).GetProperty("rowVersion").GetInt64();

        var update = await scenario.OwnerClient.PutAsJsonAsync($"{AccountsUrl}/{accountId}", new { name = "Owner investment", isActive = false, rowVersion });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        var after = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{AccountsUrl}?searchText=Owner investment"));
        var account = after.GetProperty("items").EnumerateArray().Single();
        Assert.False(account.GetProperty("isActive").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await scenario.OwnerClient.DeleteAsync($"{AccountsUrl}/{accountId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.OwnerClient.DeleteAsync($"{AccountsUrl}/{accountId}")).StatusCode);
    }

    [Fact]
    public async Task A_system_account_can_be_renamed_but_never_deleted_or_deactivated()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var cashAccount = (await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{AccountsUrl}?searchText=1010")))
            .GetProperty("items").EnumerateArray().Single(account => account.GetProperty("code").GetString() == "1010");
        var accountId = cashAccount.GetProperty("id").GetGuid();
        var rowVersion = cashAccount.GetProperty("rowVersion").GetInt64();
        Assert.Equal("CASH", cashAccount.GetProperty("systemRole").GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await scenario.OwnerClient.DeleteAsync($"{AccountsUrl}/{accountId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await scenario.OwnerClient.PutAsJsonAsync($"{AccountsUrl}/{accountId}", new { name = "Cash", isActive = false, rowVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await scenario.OwnerClient.PutAsJsonAsync($"{AccountsUrl}/{accountId}", new { name = "Cash in hand (renamed)", isActive = true, rowVersion })).StatusCode);

        // the database refuses it too, whoever asks
        var databaseRefusal = await Assert.ThrowsAnyAsync<Exception>(() => environment.Database.ExecuteAsOwnerAsync(
            "delete from acct.accounts where company_id = $1 and id = $2", scenario.CompanyId, accountId));
        Assert.Contains("system account", databaseRefusal.Message);
    }

    [Fact]
    public async Task An_account_that_has_journal_lines_cannot_be_deleted()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var accountId = await CreateAccountAsync(scenario.OwnerClient, "101", "Petty cash box");
        var openingUrl = TestScenarioBuilder.Url(ApiRoutes.OpeningBalances.Root);
        var save = await scenario.OwnerClient.PutAsJsonAsync(openingUrl, new
        {
            storeId = Guid.Empty,
            entryDate = "2026-01-01",
            lines = new[] { new { accountId = (Guid?)accountId, debitAmount = 500L, creditAmount = 0L } },
        });
        await TestScenarioBuilder.EnsureSuccessAsync(save);

        var delete = await scenario.OwnerClient.DeleteAsync($"{AccountsUrl}/{accountId}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        var problem = await OwnerCompanyScenario.ReadJsonAsync(delete);
        Assert.Equal(ErrorCodes.RecordInUse, problem.GetProperty("errorCode").GetString());
        Assert.Contains("acct.journal_lines", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Permissions_apply_per_screen_but_the_group_list_is_open_to_every_company_user()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var cashier = await scenario.SignInUserWithPermissionsAsync(RoleCodes.CashierSalesman, [scenario.FirstStoreId]);

        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync(GroupsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync(MainTypesUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync(AccountsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync(GroupsUrl, new { mainTypeCode = "ASSET", name = "Nope" })).StatusCode);

        var secondLevelEditor = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Settings.NewSecondLevel.Add);
        Assert.Equal(HttpStatusCode.Created, (await secondLevelEditor.PostAsJsonAsync(GroupsUrl, new { mainTypeCode = "ASSET", name = "Allowed group" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await secondLevelEditor.PostAsJsonAsync(AccountsUrl, new { groupCode = "101", name = "Not allowed" })).StatusCode);

        var accountViewer = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Settings.GlAccounts.View);
        Assert.Equal(HttpStatusCode.OK, (await accountViewer.GetAsync(AccountsUrl)).StatusCode);
    }

    [Fact]
    public async Task One_company_never_sees_or_changes_the_chart_of_another()
    {
        var companyA = await OwnerCompanyScenario.CreateAsync(environment);
        var companyB = await OwnerCompanyScenario.CreateAsync(environment);
        var accountOfA = await CreateAccountAsync(companyA.OwnerClient, "102", "Bank of A only");
        await OwnerCompanyScenario.PostJsonAsync(companyA.OwnerClient, GroupsUrl, new { mainTypeCode = "ASSET", name = "Group of A only" });

        var accountsOfB = await OwnerCompanyScenario.ReadJsonAsync(await companyB.OwnerClient.GetAsync($"{AccountsUrl}?searchText=of A only"));
        Assert.Equal(0, accountsOfB.GetProperty("totalCount").GetInt32());
        Assert.DoesNotContain(await ListGroupsAsync(companyB.OwnerClient), group => group.GetProperty("name").GetString() == "Group of A only");
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.DeleteAsync($"{AccountsUrl}/{accountOfA}")).StatusCode);
    }
}
