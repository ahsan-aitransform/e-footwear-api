using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using static EFootwear.Tests.Contacts.ContactTestHelpers;

namespace EFootwear.Tests.CashManagement;

/// <summary>The Cash Calculator (docx 3.6) and View Cash Detail (docx 3.7): server-side totals, one count per store and day, expected cash from the books.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class CashCountApiTests(ApiTestEnvironment environment)
{
    private static readonly string CashCountsUrl = TestScenarioBuilder.Url(ApiRoutes.CashCounts.Root);
    private static readonly string ContextUrl = TestScenarioBuilder.Url(ApiRoutes.CashCounts.Root, ApiRoutes.CashCounts.Context);

    private static object Denomination(int denomination, int noteCount) => new { denomination, noteCount };

    private static object CountBody(Guid storeId, string countDate, long openingCash, object[] denominations, long? rowVersion = null,
                                    long? payments = null, long? creditSales = null, long? creditReceived = null, string? note = null) => new
    {
        storeId,
        countDate,
        openingCash,
        denominations,
        paymentsAmount = payments,
        creditSalesAmount = creditSales,
        creditReceivedAmount = creditReceived,
        note,
        rowVersion,
    };

    private static async Task<JsonElement> SaveAsync(HttpClient client, object body) =>
        await OwnerCompanyScenario.ReadJsonAsync(await EnsureAsync(await client.PutAsJsonAsync(CashCountsUrl, body)));

    private static async Task<HttpResponseMessage> EnsureAsync(HttpResponseMessage response)
    {
        await TestScenarioBuilder.EnsureSuccessAsync(response);
        return response;
    }

    [Fact]
    public async Task The_server_adds_up_the_notes_and_the_difference_is_counted_minus_expected()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);

        var saved = await SaveAsync(scenario.OwnerClient, CountBody(scenario.FirstStoreId, "2026-11-20", 5_000, [
            Denomination(5000, 2), Denomination(1000, 1), Denomination(500, 3), Denomination(100, 0), Denomination(1, 7),
        ], payments: 800, creditSales: 1_200, creditReceived: 300, note: "Evening count"));

        Assert.Equal(12_507, saved.GetProperty("countedCash").GetInt64());
        Assert.Equal(0, saved.GetProperty("expectedCash").GetInt64());               // nothing is booked yet
        Assert.Equal(12_507, saved.GetProperty("differenceAmount").GetInt64());
        Assert.Equal(5_000, saved.GetProperty("openingCash").GetInt64());
        Assert.Equal(800, saved.GetProperty("paymentsAmount").GetInt64());
        Assert.Equal(1_200, saved.GetProperty("creditSalesAmount").GetInt64());
        Assert.Equal(300, saved.GetProperty("creditReceivedAmount").GetInt64());
        Assert.Equal("Evening count", saved.GetProperty("note").GetString());
        Assert.Equal("Test Owner", saved.GetProperty("countedByName").GetString());

        var counted = saved.GetProperty("denominations").EnumerateArray()
            .ToDictionary(line => line.GetProperty("denomination").GetInt32(), line => line.GetProperty("noteCount").GetInt32());
        Assert.Equal(new Dictionary<int, int> { [5000] = 2, [1000] = 1, [500] = 3, [1] = 7 }, counted);   // zero counts are not stored
    }

    [Fact]
    public async Task The_expected_cash_comes_from_the_cash_postings_of_the_store_up_to_the_count_date()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        await PostCashReceivedAsync(environment, scenario.CompanyId, scenario.FirstStoreId, new DateOnly(2026, 11, 18), 3_000);
        await PostCashReceivedAsync(environment, scenario.CompanyId, scenario.FirstStoreId, new DateOnly(2026, 11, 20), 2_000);
        await PostCashReceivedAsync(environment, scenario.CompanyId, scenario.FirstStoreId, new DateOnly(2026, 11, 25), 9_999);   // later: not counted yet
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        await PostCashReceivedAsync(environment, scenario.CompanyId, secondStoreId, new DateOnly(2026, 11, 19), 700);              // another store

        var context = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{ContextUrl}?storeId={scenario.FirstStoreId}&countDate=2026-11-20"));
        Assert.Equal(5_000, context.GetProperty("expectedCash").GetInt64());
        Assert.Equal(JsonValueKind.Null, context.GetProperty("existingCount").ValueKind);

        var saved = await SaveAsync(scenario.OwnerClient, CountBody(scenario.FirstStoreId, "2026-11-20", 0, [Denomination(1000, 4), Denomination(500, 2)]));
        Assert.Equal(5_000, saved.GetProperty("countedCash").GetInt64());
        Assert.Equal(5_000, saved.GetProperty("expectedCash").GetInt64());
        Assert.Equal(0, saved.GetProperty("differenceAmount").GetInt64());

        var short5 = await SaveAsync(scenario.OwnerClient, CountBody(scenario.FirstStoreId, "2026-11-19", 0, [Denomination(1000, 2)]));
        Assert.Equal(3_000, short5.GetProperty("expectedCash").GetInt64());      // only the 18th counts for the 19th
        Assert.Equal(-1_000, short5.GetProperty("differenceAmount").GetInt64());
    }

    [Fact]
    public async Task There_is_one_count_per_store_and_day_and_a_second_save_needs_the_row_version()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var first = await SaveAsync(scenario.OwnerClient, CountBody(scenario.FirstStoreId, "2026-11-20", 0, [Denomination(500, 2)]));

        var withoutVersion = await scenario.OwnerClient.PutAsJsonAsync(CashCountsUrl, CountBody(scenario.FirstStoreId, "2026-11-20", 0, [Denomination(500, 3)]));
        Assert.Equal(HttpStatusCode.Conflict, withoutVersion.StatusCode);

        var rowVersion = first.GetProperty("rowVersion").GetInt64();
        var replaced = await SaveAsync(scenario.OwnerClient, CountBody(scenario.FirstStoreId, "2026-11-20", 0, [Denomination(500, 3), Denomination(100, 1)], rowVersion));
        Assert.Equal(first.GetProperty("id").GetGuid(), replaced.GetProperty("id").GetGuid());      // the same day's count was replaced
        Assert.Equal(1_600, replaced.GetProperty("countedCash").GetInt64());
        Assert.Equal(2, replaced.GetProperty("denominations").GetArrayLength());

        var stale = await scenario.OwnerClient.PutAsJsonAsync(CashCountsUrl, CountBody(scenario.FirstStoreId, "2026-11-20", 0, [Denomination(500, 9)], rowVersion));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(ErrorCodes.ConcurrentUpdate, (await OwnerCompanyScenario.ReadJsonAsync(stale)).GetProperty("errorCode").GetString());

        Assert.Equal(1L, await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from cash.cash_counts where company_id = $1 and store_id = $2", scenario.CompanyId, scenario.FirstStoreId));
        var context = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{ContextUrl}?storeId={scenario.FirstStoreId}&countDate=2026-11-20"));
        Assert.Equal(1_600, context.GetProperty("existingCount").GetProperty("countedCash").GetInt64());
    }

    [Theory]
    [InlineData("unknownDenomination")]
    [InlineData("negativeNotes")]
    [InlineData("duplicateDenomination")]
    [InlineData("negativeOpeningCash")]
    [InlineData("negativePayments")]
    [InlineData("headOffice")]
    public async Task Invalid_counts_are_refused(string problem)
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var body = problem switch
        {
            "unknownDenomination" => CountBody(scenario.FirstStoreId, "2026-11-20", 0, [Denomination(200, 1)]),
            "negativeNotes" => CountBody(scenario.FirstStoreId, "2026-11-20", 0, [Denomination(500, -1)]),
            "duplicateDenomination" => CountBody(scenario.FirstStoreId, "2026-11-20", 0, [Denomination(500, 1), Denomination(500, 2)]),
            "negativeOpeningCash" => CountBody(scenario.FirstStoreId, "2026-11-20", -1, [Denomination(500, 1)]),
            "negativePayments" => CountBody(scenario.FirstStoreId, "2026-11-20", 0, [Denomination(500, 1)], payments: -5),
            _ => CountBody(Guid.Empty, "2026-11-20", 0, [Denomination(500, 1)]),
        };

        var response = await scenario.OwnerClient.PutAsJsonAsync(CashCountsUrl, body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0L, await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from cash.cash_counts where company_id = $1", scenario.CompanyId));
    }

    [Fact]
    public async Task View_cash_detail_lists_counts_with_store_user_and_filters_by_store_and_date()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        await SaveAsync(scenario.OwnerClient, CountBody(scenario.FirstStoreId, "2026-11-10", 0, [Denomination(1000, 1)]));
        await SaveAsync(scenario.OwnerClient, CountBody(scenario.FirstStoreId, "2026-11-20", 0, [Denomination(1000, 2)]));
        await SaveAsync(scenario.OwnerClient, CountBody(secondStoreId, "2026-11-20", 0, [Denomination(1000, 3)]));

        var all = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(CashCountsUrl));
        Assert.Equal(3, all.GetProperty("totalCount").GetInt32());

        var firstStoreOnly = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{CashCountsUrl}?storeId={scenario.FirstStoreId}&from=2026-11-15&to=2026-11-30"));
        Assert.Equal(1, firstStoreOnly.GetProperty("totalCount").GetInt32());
        var row = firstStoreOnly.GetProperty("items")[0];
        Assert.Equal("First Store", row.GetProperty("storeName").GetString());
        Assert.Equal(2_000, row.GetProperty("countedCash").GetInt64());
        Assert.Equal(0, row.GetProperty("expectedCash").GetInt64());
        Assert.Equal(2_000, row.GetProperty("differenceAmount").GetInt64());
        Assert.Equal("Test Owner", row.GetProperty("countedByName").GetString());
        Assert.Equal("2026-11-20", row.GetProperty("countDate").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.GetAsync($"{CashCountsUrl}?from=2026-12-01&to=2026-11-01")).StatusCode);
    }

    [Fact]
    public async Task Saving_needs_the_cash_count_restricted_permission_as_well_as_the_screen_permission()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var body = CountBody(scenario.FirstStoreId, "2026-11-20", 0, [Denomination(500, 2)]);

        var screenOnly = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.CashManagement.CashCalculator.View, PermissionCodes.CashManagement.CashCalculator.Add);
        var denied = await screenOnly.PutAsJsonAsync(CashCountsUrl, body);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(ErrorCodes.PermissionDenied, (await OwnerCompanyScenario.ReadJsonAsync(denied)).GetProperty("errorCode").GetString());

        var restrictedOnly = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.RestrictedOperations.Accounting.CashCount);
        Assert.Equal(HttpStatusCode.Forbidden, (await restrictedOnly.PutAsJsonAsync(CashCountsUrl, body)).StatusCode);

        var allowed = await scenario.SignInManagerWithPermissionsAsync(
            PermissionCodes.CashManagement.CashCalculator.Add, PermissionCodes.RestrictedOperations.Accounting.CashCount);
        Assert.Equal(HttpStatusCode.OK, (await allowed.PutAsJsonAsync(CashCountsUrl, body)).StatusCode);
    }

    [Fact]
    public async Task Reading_counts_needs_the_view_permissions_and_a_store_user_stays_in_their_stores()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        await SaveAsync(scenario.OwnerClient, CountBody(secondStoreId, "2026-11-20", 0, [Denomination(500, 1)]));
        var nothing = await scenario.SignInUserWithPermissionsAsync(RoleCodes.CashierSalesman, [scenario.FirstStoreId]);

        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.GetAsync(CashCountsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.GetAsync($"{ContextUrl}?storeId={scenario.FirstStoreId}&countDate=2026-11-20")).StatusCode);

        var manager = await scenario.SignInManagerWithPermissionsAsync(
            PermissionCodes.CashManagement.ViewCashDetail.View, PermissionCodes.CashManagement.CashCalculator.View, PermissionCodes.CashManagement.CashCalculator.Add,
            PermissionCodes.RestrictedOperations.Accounting.CashCount);
        Assert.Equal(0, (await OwnerCompanyScenario.ReadJsonAsync(await manager.GetAsync(CashCountsUrl))).GetProperty("totalCount").GetInt32());   // the other store's count is hidden
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync($"{CashCountsUrl}?storeId={secondStoreId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync($"{ContextUrl}?storeId={secondStoreId}&countDate=2026-11-20")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsJsonAsync(CashCountsUrl, CountBody(secondStoreId, "2026-11-21", 0, [Denomination(500, 1)]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.PutAsJsonAsync(CashCountsUrl, CountBody(scenario.FirstStoreId, "2026-11-21", 0, [Denomination(500, 1)]))).StatusCode);
    }

    [Fact]
    public async Task One_company_never_sees_or_writes_the_cash_counts_of_another()
    {
        var companyA = await OwnerCompanyScenario.CreateAsync(environment);
        var companyB = await OwnerCompanyScenario.CreateAsync(environment);
        await SaveAsync(companyA.OwnerClient, CountBody(companyA.FirstStoreId, "2026-11-20", 0, [Denomination(1000, 5)]));

        Assert.Equal(0, (await OwnerCompanyScenario.ReadJsonAsync(await companyB.OwnerClient.GetAsync(CashCountsUrl))).GetProperty("totalCount").GetInt32());
        var contextOfB = await OwnerCompanyScenario.ReadJsonAsync(await companyB.OwnerClient.GetAsync($"{ContextUrl}?storeId={companyA.FirstStoreId}&countDate=2026-11-20"));
        Assert.Equal(JsonValueKind.Null, contextOfB.GetProperty("existingCount").ValueKind);   // nothing of A is visible
        Assert.Equal(HttpStatusCode.BadRequest, (await companyB.OwnerClient.PutAsJsonAsync(CashCountsUrl,
            CountBody(companyA.FirstStoreId, "2026-11-20", 0, [Denomination(1000, 1)]))).StatusCode);
    }
}
