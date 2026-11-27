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

namespace EFootwear.Tests.Staff;

/// <summary>New Staff, View Staff, plus store transfers: validation, codes, search, store history, deletion, permissions, tenant isolation.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class StaffMemberApiTests(ApiTestEnvironment environment)
{
    private static readonly string LookupUrl = TestScenarioBuilder.Url(ApiRoutes.StaffMembers.Root, ApiRoutes.StaffMembers.Lookup);
    private static readonly string CitiesUrl = TestScenarioBuilder.Url(ApiRoutes.MasterData.Cities);

    // A new staff member's first assignment is recorded with today's date, so a transfer must be dated later to be the newest history row.
    private static readonly string TomorrowIsoDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1).ToString("yyyy-MM-dd");
    private static readonly string DayAfterTomorrowIsoDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2).ToString("yyyy-MM-dd");

    private static async Task<JsonElement> ListAsync(HttpClient client, string query = "") =>
        await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(StaffMembersUrl + query));

    private static string[] Names(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!).ToArray();

    [Fact]
    public async Task A_staff_member_is_created_with_a_code_a_store_and_a_first_history_row()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var cityId = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, CitiesUrl, new { name = "Sahiwal" })).GetProperty("id").GetGuid();

        var staffId = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, StaffMembersUrl, StaffMemberBody(
            "  Imran   Khan ", scenario.FirstStoreId, email: "imran@example.test", birthDate: "1992-02-02", cityId: cityId, designation: "Manager", isManager: true))).GetProperty("id").GetGuid();

        var staff = await GetStaffMemberAsync(scenario.OwnerClient, staffId);
        Assert.Matches(@"^103\d{5}$", staff.GetProperty("code").GetString()!);
        Assert.Equal("Imran Khan", staff.GetProperty("name").GetString());
        Assert.Equal("Manager", staff.GetProperty("designation").GetString());
        Assert.True(staff.GetProperty("isManager").GetBoolean());
        Assert.Equal(scenario.FirstStoreId, staff.GetProperty("storeId").GetGuid());
        Assert.Equal("Sahiwal", staff.GetProperty("cityName").GetString());
        Assert.Equal("1992-02-02", staff.GetProperty("birthDate").GetString());
        Assert.Equal(0, staff.GetProperty("balanceAmount").GetInt64());

        var history = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{StaffMembersUrl}/{staffId}/transfers"));
        Assert.Equal(1, history.GetArrayLength());   // the first assignment is recorded
        Assert.Equal(JsonValueKind.Null, history[0].GetProperty("fromStoreId").ValueKind);
    }

    [Theory]
    [InlineData("noName")]
    [InlineData("noPhone")]
    [InlineData("badEmail")]
    [InlineData("badPhone")]
    [InlineData("futureBirthDate")]
    [InlineData("badStatus")]
    [InlineData("unknownCity")]
    [InlineData("noStore")]
    public async Task Invalid_staff_are_refused_with_a_validation_error(string problem)
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var store = scenario.FirstStoreId;
        object body = problem switch
        {
            "noName" => StaffMemberBody("   ", store),
            "noPhone" => StaffMemberBody("No Phone", store, phone: null),
            "badEmail" => StaffMemberBody("Bad Email", store, email: "not-an-email"),
            "badPhone" => StaffMemberBody("Bad Phone", store, phone: "call me"),
            "futureBirthDate" => StaffMemberBody("Born Tomorrow", store, birthDate: DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd")),
            "badStatus" => StaffMemberBody("Bad Status", store, statusCode: "SLEEPING"),
            "unknownCity" => StaffMemberBody("Unknown City", store, cityId: Guid.NewGuid()),
            _ => StaffMemberBody("Without Store", null),
        };

        var response = await scenario.OwnerClient.PostAsJsonAsync(StaffMembersUrl, body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, (await OwnerCompanyScenario.ReadJsonAsync(response)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task View_staff_filters_by_store_city_and_status_and_search_finds_by_name_code_phone_and_designation()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        var multan = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, CitiesUrl, new { name = "Multan" })).GetProperty("id").GetGuid();
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, StaffMembersUrl, StaffMemberBody("Shop Salesman", scenario.FirstStoreId, designation: "Salesman", cityId: multan, phone: "0309 5550001"));
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, StaffMembersUrl, StaffMemberBody("Second Store Cashier", secondStoreId, designation: "Cashier", phone: "0308 5550002"));
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, StaffMembersUrl, StaffMemberBody("Sleeping Staff", scenario.FirstStoreId, statusCode: "INACTIVE"));

        Assert.Equal(new[] { "Second Store Cashier" }, Names(await ListAsync(scenario.OwnerClient, $"?storeId={secondStoreId}")));
        Assert.Equal(new[] { "Shop Salesman" }, Names(await ListAsync(scenario.OwnerClient, $"?cityId={multan}")));
        Assert.Equal(new[] { "Sleeping Staff" }, Names(await ListAsync(scenario.OwnerClient, "?statusCode=INACTIVE")));
        Assert.Equal(1, (await ListAsync(scenario.OwnerClient, "?searchText=5550002")).GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { "Shop Salesman" }, Names(await ListAsync(scenario.OwnerClient, "?searchText=salesman")));
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.GetAsync($"{StaffMembersUrl}?statusCode=ANIMAL")).StatusCode);

        var row = (await ListAsync(scenario.OwnerClient, "?searchText=cashier")).GetProperty("items")[0];
        Assert.Equal("Cashier", row.GetProperty("designation").GetString());
        Assert.Equal("Store S2", row.GetProperty("storeName").GetString());

        var viaSearch = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{StaffMembersUrl}?searchText=sleeping"));
        Assert.Equal(new[] { "Sleeping Staff" }, Names(viaSearch));
    }

    [Fact]
    public async Task A_staff_member_is_edited_with_its_row_version_and_the_store_does_not_change_by_edit()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        var staffId = await CreateStaffMemberAsync(scenario.OwnerClient, "Before", scenario.FirstStoreId);
        var loaded = await GetStaffMemberAsync(scenario.OwnerClient, staffId);
        var rowVersion = loaded.GetProperty("rowVersion").GetInt64();

        var edit = await scenario.OwnerClient.PutAsJsonAsync($"{StaffMembersUrl}/{staffId}",
            StaffMemberBody("After", secondStoreId, designation: "Senior Salesman", rowVersion: rowVersion));
        Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);
        var reloaded = await GetStaffMemberAsync(scenario.OwnerClient, staffId);
        Assert.Equal("After", reloaded.GetProperty("name").GetString());
        Assert.Equal("Senior Salesman", reloaded.GetProperty("designation").GetString());
        Assert.Equal(scenario.FirstStoreId, reloaded.GetProperty("storeId").GetGuid());   // only a transfer changes the store
        Assert.Equal(loaded.GetProperty("code").GetString(), reloaded.GetProperty("code").GetString());

        var stale = await scenario.OwnerClient.PutAsJsonAsync($"{StaffMembersUrl}/{staffId}", StaffMemberBody("Late edit", scenario.FirstStoreId, rowVersion: rowVersion));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(ErrorCodes.ConcurrentUpdate, (await OwnerCompanyScenario.ReadJsonAsync(stale)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_staff_member_can_be_moved_to_another_store_and_the_history_keeps_every_move()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        var staffId = await CreateStaffMemberAsync(scenario.OwnerClient, "Imran", scenario.FirstStoreId, "Manager");

        var transfersUrl = $"{StaffMembersUrl}/{staffId}/transfers";
        var transfer = await scenario.OwnerClient.PostAsJsonAsync(transfersUrl, new { toStoreId = secondStoreId, effectiveOn = TomorrowIsoDate });
        Assert.Equal(HttpStatusCode.NoContent, transfer.StatusCode);

        Assert.Equal(secondStoreId, (await GetStaffMemberAsync(scenario.OwnerClient, staffId)).GetProperty("storeId").GetGuid());
        var history = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(transfersUrl));
        Assert.Equal(2, history.GetArrayLength());
        Assert.Equal(scenario.FirstStoreId, history[0].GetProperty("fromStoreId").GetGuid());      // newest first
        Assert.Equal(secondStoreId, history[0].GetProperty("toStoreId").GetGuid());
        Assert.Equal(TomorrowIsoDate, history[0].GetProperty("effectiveOn").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PostAsJsonAsync(transfersUrl, new { toStoreId = secondStoreId, effectiveOn = DayAfterTomorrowIsoDate })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PostAsJsonAsync(transfersUrl, new { toStoreId = Guid.Empty, effectiveOn = DayAfterTomorrowIsoDate })).StatusCode);
    }

    [Fact]
    public async Task A_transfer_needs_its_own_permission_both_stores_and_an_existing_staff_member()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        var customerId = await CreateCustomerAsync(scenario.OwnerClient, "Just a customer");
        var staffId = await CreateStaffMemberAsync(scenario.OwnerClient, "Salesman", scenario.FirstStoreId);

        // a customer id is not a staff member
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.OwnerClient.PostAsJsonAsync(
            $"{StaffMembersUrl}/{customerId}/transfers", new { toStoreId = secondStoreId, effectiveOn = TomorrowIsoDate })).StatusCode);

        var noPermission = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.HumanResources.NewStaff.View);
        Assert.Equal(HttpStatusCode.Forbidden, (await noPermission.PostAsJsonAsync(
            $"{StaffMembersUrl}/{staffId}/transfers", new { toStoreId = secondStoreId, effectiveOn = TomorrowIsoDate })).StatusCode);

        // allowed to transfer, but works only in the first store: the destination is out of reach
        var storeManager = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.HumanResources.StaffTransfers.Add);
        Assert.Equal(HttpStatusCode.Forbidden, (await storeManager.PostAsJsonAsync(
            $"{StaffMembersUrl}/{staffId}/transfers", new { toStoreId = secondStoreId, effectiveOn = TomorrowIsoDate })).StatusCode);
    }

    [Fact]
    public async Task A_staff_member_without_history_is_deleted_softly_and_one_with_accounting_history_is_protected()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var lonelyId = await CreateStaffMemberAsync(scenario.OwnerClient, "Lonely", scenario.FirstStoreId);
        var busyId = await CreateStaffMemberAsync(scenario.OwnerClient, "Busy", scenario.FirstStoreId);
        await PostStaffLedgerEntryAsync(environment, scenario.CompanyId, busyId, new DateOnly(2026, 3, 1), 1_000, 0);

        Assert.Equal(HttpStatusCode.NoContent, (await scenario.OwnerClient.DeleteAsync($"{StaffMembersUrl}/{lonelyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.OwnerClient.GetAsync($"{StaffMembersUrl}/{lonelyId}")).StatusCode);
        Assert.DoesNotContain("Lonely", Names(await ListAsync(scenario.OwnerClient)));

        var protectedStaff = await scenario.OwnerClient.DeleteAsync($"{StaffMembersUrl}/{busyId}");
        Assert.Equal(HttpStatusCode.Conflict, protectedStaff.StatusCode);
        var problem = await OwnerCompanyScenario.ReadJsonAsync(protectedStaff);
        Assert.Equal(ErrorCodes.RecordInUse, problem.GetProperty("errorCode").GetString());
        Assert.Contains("acct.journal_lines", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task An_opening_balance_with_a_staff_member_feeds_the_balance_and_needs_the_opening_balance_permission()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var staffId = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, StaffMembersUrl, StaffMemberBody(
            "Staff with opening", scenario.FirstStoreId, openingBalance: new { sideCode = "DEBIT", amount = 2_000L, asOfDate = "2026-01-01" }))).GetProperty("id").GetGuid();
        Assert.Equal(2_000, (await GetStaffMemberAsync(scenario.OwnerClient, staffId)).GetProperty("balanceAmount").GetInt64());
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));

        var withoutRestriction = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.HumanResources.NewStaff.Add, PermissionCodes.HumanResources.NewStaff.View);
        var denied = await withoutRestriction.PostAsJsonAsync(StaffMembersUrl, StaffMemberBody(
            "Needs permission", scenario.FirstStoreId, openingBalance: new { sideCode = "DEBIT", amount = 100L, asOfDate = "2026-01-01" }));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(0, (await ListAsync(scenario.OwnerClient, "?searchText=Needs permission")).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Each_staff_action_needs_its_own_permission_but_the_lookup_is_open_to_every_company_user()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var staffId = await CreateStaffMemberAsync(scenario.OwnerClient, "Walk in friend", scenario.FirstStoreId);
        var nothing = await scenario.SignInUserWithPermissionsAsync(RoleCodes.CashierSalesman, [scenario.FirstStoreId]);

        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.GetAsync(StaffMembersUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.GetAsync($"{StaffMembersUrl}/{staffId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.PostAsJsonAsync(StaffMembersUrl, StaffMemberBody("Nope", scenario.FirstStoreId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.DeleteAsync($"{StaffMembersUrl}/{staffId}")).StatusCode);

        var lookup = await OwnerCompanyScenario.ReadJsonAsync(await nothing.GetAsync($"{LookupUrl}?searchText=friend"));
        Assert.Equal(new[] { "Walk in friend" }, lookup.EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToArray());

        var adder = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.HumanResources.NewStaff.Add);
        Assert.Equal(HttpStatusCode.Created, (await adder.PostAsJsonAsync(StaffMembersUrl, StaffMemberBody("Allowed", scenario.FirstStoreId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await adder.GetAsync(StaffMembersUrl)).StatusCode);
    }

    [Fact]
    public async Task One_company_never_sees_or_changes_the_staff_of_another()
    {
        var companyA = await OwnerCompanyScenario.CreateAsync(environment);
        var companyB = await OwnerCompanyScenario.CreateAsync(environment);
        var staffOfA = await CreateStaffMemberAsync(companyA.OwnerClient, "Staff of A only", companyA.FirstStoreId);

        Assert.DoesNotContain("Staff of A only", Names(await ListAsync(companyB.OwnerClient)));
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.GetAsync($"{StaffMembersUrl}/{staffOfA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.DeleteAsync($"{StaffMembersUrl}/{staffOfA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.PutAsJsonAsync($"{StaffMembersUrl}/{staffOfA}", StaffMemberBody("Hijacked", companyB.FirstStoreId, rowVersion: 1))).StatusCode);
        var lookupOfB = await OwnerCompanyScenario.ReadJsonAsync(await companyB.OwnerClient.GetAsync($"{LookupUrl}?searchText=Staff of A only"));
        Assert.Equal(0, lookupOfB.GetArrayLength());
    }
}
