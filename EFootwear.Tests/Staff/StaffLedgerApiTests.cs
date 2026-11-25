using System.Net;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using static EFootwear.Tests.Contacts.ContactTestHelpers;

namespace EFootwear.Tests.Staff;

/// <summary>Staff Account Detail: the ledger is read only, derived from the journal, and the balance always equals it.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class StaffLedgerApiTests(ApiTestEnvironment environment)
{
    private static readonly string LedgerUrl = TestScenarioBuilder.Url(ApiRoutes.StaffLedger.Root);

    private static async Task<JsonElement> GetLedgerAsync(HttpClient client, string query) =>
        await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(LedgerUrl + query));

    private static long[] RunningBalances(JsonElement ledger) =>
        ledger.GetProperty("lines").GetProperty("items").EnumerateArray().Select(line => line.GetProperty("runningBalanceAmount").GetInt64()).ToArray();

    private static async Task<Guid> CreateStaffOf(OwnerCompanyScenario scenario, string name) => await CreateStaffMemberAsync(scenario.OwnerClient, name, scenario.FirstStoreId);

    /// <summary>A staff with a debit of 1000 on 1 March, a credit of 400 on 10 March and a debit of 250 on 5 April.</summary>
    private async Task<(OwnerCompanyScenario Scenario, Guid StaffId)> CreateStaffWithThreeEntriesAsync()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var staffId = await CreateStaffOf(scenario, "Ledger Staff");
        await PostStaffLedgerEntryAsync(environment, scenario.CompanyId, staffId, new DateOnly(2026, 3, 1), 1_000, 0);
        await PostStaffLedgerEntryAsync(environment, scenario.CompanyId, staffId, new DateOnly(2026, 3, 10), 0, 400);
        await PostStaffLedgerEntryAsync(environment, scenario.CompanyId, staffId, new DateOnly(2026, 4, 5), 250, 0);
        return (scenario, staffId);
    }

    [Fact]
    public async Task The_ledger_of_a_staff_shows_lines_in_date_order_with_running_balance_and_totals()
    {
        var (scenario, staffId) = await CreateStaffWithThreeEntriesAsync();

        var ledger = await GetLedgerAsync(scenario.OwnerClient, $"?staffId={staffId}");
        Assert.Equal(3, ledger.GetProperty("lines").GetProperty("totalCount").GetInt32());
        Assert.Equal([1_000L, 600L, 850L], RunningBalances(ledger));
        Assert.Equal(1_250, ledger.GetProperty("totalDebitAmount").GetInt64());
        Assert.Equal(400, ledger.GetProperty("totalCreditAmount").GetInt64());
        Assert.Equal(0, ledger.GetProperty("openingBalanceAmount").GetInt64());
        Assert.Equal(850, ledger.GetProperty("closingBalanceAmount").GetInt64());

        var firstLine = ledger.GetProperty("lines").GetProperty("items")[0];
        Assert.Equal("2026-03-01", firstLine.GetProperty("entryDate").GetString());
        Assert.Equal("TEST_ENTRY", firstLine.GetProperty("sourceType").GetString());
        Assert.Equal("Ledger Staff", firstLine.GetProperty("staffName").GetString());
        Assert.Equal(staffId, firstLine.GetProperty("staffId").GetGuid());
    }

    [Fact]
    public async Task A_period_starts_from_the_balance_before_it_and_the_running_balance_keeps_the_full_history()
    {
        var (scenario, staffId) = await CreateStaffWithThreeEntriesAsync();

        var fromMarchFifth = await GetLedgerAsync(scenario.OwnerClient, $"?staffId={staffId}&from=2026-03-05");
        Assert.Equal(1_000, fromMarchFifth.GetProperty("openingBalanceAmount").GetInt64());
        Assert.Equal([600L, 850L], RunningBalances(fromMarchFifth));
        Assert.Equal(250, fromMarchFifth.GetProperty("totalDebitAmount").GetInt64());
        Assert.Equal(400, fromMarchFifth.GetProperty("totalCreditAmount").GetInt64());
        Assert.Equal(850, fromMarchFifth.GetProperty("closingBalanceAmount").GetInt64());

        var untilEndOfMarch = await GetLedgerAsync(scenario.OwnerClient, $"?staffId={staffId}&to=2026-03-31");
        Assert.Equal([1_000L, 600L], RunningBalances(untilEndOfMarch));
        Assert.Equal(600, untilEndOfMarch.GetProperty("closingBalanceAmount").GetInt64());

        var inApril = await GetLedgerAsync(scenario.OwnerClient, $"?staffId={staffId}&from=2026-04-01&to=2026-04-30");
        Assert.Equal(600, inApril.GetProperty("openingBalanceAmount").GetInt64());
        Assert.Equal([850L], RunningBalances(inApril));
    }

    [Fact]
    public async Task The_staff_balance_the_closing_balance_and_the_journal_always_agree()
    {
        var (scenario, staffId) = await CreateStaffWithThreeEntriesAsync();

        var ledger = await GetLedgerAsync(scenario.OwnerClient, $"?staffId={staffId}");
        var shownBalance = (await GetStaffMemberAsync(scenario.OwnerClient, staffId)).GetProperty("balanceAmount").GetInt64();
        var fromJournal = await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select coalesce(sum(debit - credit), 0)::bigint from acct.journal_lines where company_id = $1 and staff_id = $2", scenario.CompanyId, staffId);
        var projection = await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select balance from contacts.staff_balances where company_id = $1 and staff_id = $2", scenario.CompanyId, staffId);

        Assert.Equal(850, ledger.GetProperty("closingBalanceAmount").GetInt64());
        Assert.Equal(850, shownBalance);
        Assert.Equal(850, fromJournal);
        Assert.Equal(850, projection);
    }

    [Fact]
    public async Task The_ledger_of_all_staff_pages_through_every_staff_line()
    {
        var (scenario, _) = await CreateStaffWithThreeEntriesAsync();
        var otherStaffId = await CreateStaffOf(scenario, "Other Staff");
        await PostStaffLedgerEntryAsync(environment, scenario.CompanyId, otherStaffId, new DateOnly(2026, 3, 2), 70, 0);

        var all = await GetLedgerAsync(scenario.OwnerClient, "?pageNumber=1&pageSize=3");
        Assert.Equal(4, all.GetProperty("lines").GetProperty("totalCount").GetInt32());
        Assert.Equal(3, all.GetProperty("lines").GetProperty("items").GetArrayLength());
        Assert.Equal(1_320, all.GetProperty("totalDebitAmount").GetInt64());
        Assert.Equal(0, all.GetProperty("openingBalanceAmount").GetInt64());   // only a single staff has an opening balance

        var secondPage = await GetLedgerAsync(scenario.OwnerClient, "?pageNumber=2&pageSize=3");
        Assert.Equal(1, secondPage.GetProperty("lines").GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Bad_filters_are_refused_and_an_unknown_staff_is_not_found()
    {
        var (scenario, staffId) = await CreateStaffWithThreeEntriesAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.GetAsync($"{LedgerUrl}?staffId={staffId}&from=2026-05-01&to=2026-04-01")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.OwnerClient.GetAsync($"{LedgerUrl}?staffId={Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task The_ledger_needs_the_account_detail_permission()
    {
        var (scenario, staffId) = await CreateStaffWithThreeEntriesAsync();

        var withoutPermission = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.HumanResources.NewStaff.View);
        Assert.Equal(HttpStatusCode.Forbidden, (await withoutPermission.GetAsync($"{LedgerUrl}?staffId={staffId}")).StatusCode);

        var viewer = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.HumanResources.StaffAccountDetail.View);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{LedgerUrl}?staffId={staffId}")).StatusCode);
    }

    [Fact]
    public async Task One_company_never_sees_the_ledger_of_another()
    {
        var (companyA, staffOfA) = await CreateStaffWithThreeEntriesAsync();
        var companyB = await OwnerCompanyScenario.CreateAsync(environment);

        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.GetAsync($"{LedgerUrl}?staffId={staffOfA}")).StatusCode);
        var everythingOfB = await GetLedgerAsync(companyB.OwnerClient, "");
        Assert.Equal(0, everythingOfB.GetProperty("lines").GetProperty("totalCount").GetInt32());
        Assert.Equal(3, (await GetLedgerAsync(companyA.OwnerClient, $"?staffId={staffOfA}")).GetProperty("lines").GetProperty("totalCount").GetInt32());
    }
}
