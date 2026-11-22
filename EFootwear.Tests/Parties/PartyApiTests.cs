using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using static EFootwear.Tests.Parties.PartyTestHelpers;

namespace EFootwear.Tests.Parties;

/// <summary>New Party, Manage Party and Search Party (docx 3.1 to 3.3, Part 8): validation, codes, search, staff, deletion, tenant isolation.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class PartyApiTests(ApiTestEnvironment environment)
{
    private static readonly string SearchUrl = TestScenarioBuilder.Url(ApiRoutes.Parties.Root, ApiRoutes.Parties.Search);
    private static readonly string LookupUrl = TestScenarioBuilder.Url(ApiRoutes.Parties.Root, ApiRoutes.Parties.Lookup);
    private static readonly string CitiesUrl = TestScenarioBuilder.Url(ApiRoutes.MasterData.Cities);

    // A new staff member's first assignment is recorded with today's date, so a transfer must be dated later to be the newest history row.
    private static readonly string TomorrowIsoDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1).ToString("yyyy-MM-dd");
    private static readonly string DayAfterTomorrowIsoDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2).ToString("yyyy-MM-dd");

    private static async Task<JsonElement> ListAsync(HttpClient client, string query = "") =>
        await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(PartiesUrl + query));

    private static string[] Names(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!).ToArray();

    [Fact]
    public async Task A_customer_is_created_with_a_generated_code_and_all_its_fields_come_back()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var cityId = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, CitiesUrl, new { name = "Sahiwal" })).GetProperty("id").GetGuid();

        var partyId = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, PartiesUrl, PartyBody(
            "  Muhammad   Ali ", cnic: "35202-1234567-1", cityId: cityId, primaryPhone: "0300 1234567", email: "ali@example.test",
            creditLimit: 50_000, fatherNameUrdu: "والد", addressUrdu: "پتہ", notes: "Pays on Fridays"))).GetProperty("id").GetGuid();

        var party = await GetPartyAsync(scenario.OwnerClient, partyId);
        Assert.Matches(@"^103\d{5}$", party.GetProperty("code").GetString()!);
        Assert.Equal("Muhammad Ali", party.GetProperty("name").GetString());
        Assert.Equal("35202-1234567-1", party.GetProperty("cnic").GetString());
        Assert.Equal("Sahiwal", party.GetProperty("cityName").GetString());
        Assert.Equal(50_000, party.GetProperty("creditLimit").GetInt64());
        Assert.True(party.GetProperty("isCustomer").GetBoolean());
        Assert.False(party.GetProperty("isVendor").GetBoolean());
        Assert.Equal("ACTIVE", party.GetProperty("statusCode").GetString());
        Assert.Equal(0, party.GetProperty("balanceAmount").GetInt64());
        Assert.Equal("والد", party.GetProperty("fatherNameUrdu").GetString());
        Assert.Equal("پتہ", party.GetProperty("addressUrdu").GetString());
        Assert.Equal("Pays on Fridays", party.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task Party_codes_are_generated_one_after_another()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var first = await GetPartyAsync(scenario.OwnerClient, await CreatePartyAsync(scenario.OwnerClient, "First"));
        var second = await GetPartyAsync(scenario.OwnerClient, await CreatePartyAsync(scenario.OwnerClient, "Second"));

        var firstNumber = long.Parse(first.GetProperty("code").GetString()!);
        var secondNumber = long.Parse(second.GetProperty("code").GetString()!);
        Assert.Equal(firstNumber + 1, secondNumber);
    }

    [Theory]
    [InlineData("noType")]
    [InlineData("badCnic")]
    [InlineData("badEmail")]
    [InlineData("badPhone")]
    [InlineData("negativeCreditLimit")]
    [InlineData("noName")]
    [InlineData("badStatus")]
    [InlineData("staffWithoutStore")]
    [InlineData("unknownCity")]
    public async Task Invalid_parties_are_refused_with_a_validation_error(string problem)
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        object body = problem switch
        {
            "noType" => PartyBody("Nobody", isCustomer: false),
            "badCnic" => PartyBody("Bad Cnic", cnic: "123456789"),
            "badEmail" => PartyBody("Bad Email", email: "not-an-email"),
            "badPhone" => PartyBody("Bad Phone", primaryPhone: "call me"),
            "negativeCreditLimit" => PartyBody("Negative", creditLimit: -1),
            "noName" => PartyBody("   "),
            "badStatus" => PartyBody("Bad Status", statusCode: "SLEEPING"),
            "staffWithoutStore" => PartyBody("Staff", isCustomer: false, isStaff: true),
            _ => PartyBody("Unknown City", cityId: Guid.NewGuid()),
        };

        var response = await scenario.OwnerClient.PostAsJsonAsync(PartiesUrl, body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, (await OwnerCompanyScenario.ReadJsonAsync(response)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_cnic_belongs_to_one_party_only()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, PartiesUrl, PartyBody("Owner of the cnic", cnic: "35202-7654321-9"));

        var duplicate = await scenario.OwnerClient.PostAsJsonAsync(PartiesUrl, PartyBody("Second claimant", cnic: "35202-7654321-9"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Manage_party_filters_by_type_city_and_status_and_search_finds_by_name_code_and_phone()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var multan = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, CitiesUrl, new { name = "Multan" })).GetProperty("id").GetGuid();
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, PartiesUrl, PartyBody("Bata Distributors", isCustomer: false, isVendor: true, cityId: multan, primaryPhone: "0301 5550001"));
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, PartiesUrl, PartyBody("Retail Customer", primaryPhone: "0302 5550002"));
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, PartiesUrl, PartyBody("Sleeping Customer", statusCode: "INACTIVE"));
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, PartiesUrl, PartyBody("Shop Salesman", isCustomer: false, isStaff: true, staffStoreId: scenario.FirstStoreId, designation: "Salesman"));

        Assert.Equal(["Bata Distributors"], Names(await ListAsync(scenario.OwnerClient, "?partyTypeCode=VENDOR")));
        Assert.Equal(["Shop Salesman"], Names(await ListAsync(scenario.OwnerClient, "?partyTypeCode=STAFF")));
        Assert.Equal(["Bata Distributors"], Names(await ListAsync(scenario.OwnerClient, $"?cityId={multan}")));
        Assert.Equal(["Sleeping Customer"], Names(await ListAsync(scenario.OwnerClient, "?statusCode=INACTIVE")));
        Assert.Equal(1, (await ListAsync(scenario.OwnerClient, "?searchText=0302")).GetProperty("totalCount").GetInt32());
        Assert.Equal(["Shop Salesman"], Names(await ListAsync(scenario.OwnerClient, "?searchText=salesman")));
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.GetAsync($"{PartiesUrl}?partyTypeCode=ANIMAL")).StatusCode);

        var staffRow = (await ListAsync(scenario.OwnerClient, "?partyTypeCode=STAFF")).GetProperty("items")[0];
        Assert.Equal("Salesman", staffRow.GetProperty("designation").GetString());

        var sorted = Names(await ListAsync(scenario.OwnerClient, "?sortField=name&sortDirection=Descending"));
        Assert.Equal(sorted.OrderByDescending(name => name, StringComparer.Ordinal).ToArray(), sorted);

        // Search Party is its own screen with its own endpoint
        var viaSearch = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{SearchUrl}?searchText=bata"));
        Assert.Equal(["Bata Distributors"], Names(viaSearch));
    }

    [Fact]
    public async Task A_party_is_edited_with_its_row_version_and_a_stale_version_is_refused()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var partyId = await CreatePartyAsync(scenario.OwnerClient, "Before");
        var loaded = await GetPartyAsync(scenario.OwnerClient, partyId);
        var rowVersion = loaded.GetProperty("rowVersion").GetInt64();

        var edit = await scenario.OwnerClient.PutAsJsonAsync($"{PartiesUrl}/{partyId}", PartyBody("After", isCustomer: true, isVendor: true, rowVersion: rowVersion));
        Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);
        var reloaded = await GetPartyAsync(scenario.OwnerClient, partyId);
        Assert.Equal("After", reloaded.GetProperty("name").GetString());
        Assert.True(reloaded.GetProperty("isVendor").GetBoolean());
        Assert.Equal(loaded.GetProperty("code").GetString(), reloaded.GetProperty("code").GetString());   // the code never changes

        var stale = await scenario.OwnerClient.PutAsJsonAsync($"{PartiesUrl}/{partyId}", PartyBody("Late edit", rowVersion: rowVersion));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(ErrorCodes.ConcurrentUpdate, (await OwnerCompanyScenario.ReadJsonAsync(stale)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_staff_party_has_a_store_and_a_history_and_can_be_moved_to_another_store()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        var staffId = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, PartiesUrl, PartyBody(
            "Imran", isCustomer: false, isStaff: true, staffStoreId: scenario.FirstStoreId, designation: "Manager"))).GetProperty("id").GetGuid();

        var staff = await GetPartyAsync(scenario.OwnerClient, staffId);
        Assert.Equal(scenario.FirstStoreId, staff.GetProperty("staffStoreId").GetGuid());
        Assert.Equal("Manager", staff.GetProperty("designation").GetString());

        var transfersUrl = $"{PartiesUrl}/{staffId}/staff-transfers";
        var firstHistory = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(transfersUrl));
        Assert.Equal(1, firstHistory.GetArrayLength());           // the first assignment is recorded
        Assert.Equal(JsonValueKind.Null, firstHistory[0].GetProperty("fromStoreId").ValueKind);

        var transfer = await scenario.OwnerClient.PostAsJsonAsync(transfersUrl, new { toStoreId = secondStoreId, effectiveOn = TomorrowIsoDate });
        Assert.Equal(HttpStatusCode.NoContent, transfer.StatusCode);

        Assert.Equal(secondStoreId, (await GetPartyAsync(scenario.OwnerClient, staffId)).GetProperty("staffStoreId").GetGuid());
        var history = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(transfersUrl));
        Assert.Equal(2, history.GetArrayLength());
        Assert.Equal(scenario.FirstStoreId, history[0].GetProperty("fromStoreId").GetGuid());      // newest first
        Assert.Equal(secondStoreId, history[0].GetProperty("toStoreId").GetGuid());
        Assert.Equal(TomorrowIsoDate, history[0].GetProperty("effectiveOn").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PostAsJsonAsync(transfersUrl, new { toStoreId = secondStoreId, effectiveOn = DayAfterTomorrowIsoDate })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PostAsJsonAsync(transfersUrl, new { toStoreId = Guid.Empty, effectiveOn = DayAfterTomorrowIsoDate })).StatusCode);
    }

    [Fact]
    public async Task Only_a_staff_party_can_be_transferred_and_the_transfer_needs_its_own_permission_and_both_stores()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        var customerId = await CreatePartyAsync(scenario.OwnerClient, "Just a customer");
        var staffId = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, PartiesUrl, PartyBody(
            "Salesman", isCustomer: false, isStaff: true, staffStoreId: scenario.FirstStoreId))).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PostAsJsonAsync(
            $"{PartiesUrl}/{customerId}/staff-transfers", new { toStoreId = secondStoreId, effectiveOn = TomorrowIsoDate })).StatusCode);

        var noPermission = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Party.NewParty.View);
        Assert.Equal(HttpStatusCode.Forbidden, (await noPermission.PostAsJsonAsync(
            $"{PartiesUrl}/{staffId}/staff-transfers", new { toStoreId = secondStoreId, effectiveOn = TomorrowIsoDate })).StatusCode);

        // allowed to transfer, but works only in the first store: the destination is out of reach
        var storeManager = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.HumanResources.StaffTransfers.Add);
        Assert.Equal(HttpStatusCode.Forbidden, (await storeManager.PostAsJsonAsync(
            $"{PartiesUrl}/{staffId}/staff-transfers", new { toStoreId = secondStoreId, effectiveOn = TomorrowIsoDate })).StatusCode);
    }

    [Fact]
    public async Task A_party_without_history_is_deleted_softly_and_a_party_with_accounting_history_is_protected()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var lonelyId = await CreatePartyAsync(scenario.OwnerClient, "Lonely");
        var busyId = await CreatePartyAsync(scenario.OwnerClient, "Busy");
        await PostLedgerEntryAsync(environment, scenario.CompanyId, busyId, new DateOnly(2026, 3, 1), 1_000, 0);

        Assert.Equal(HttpStatusCode.NoContent, (await scenario.OwnerClient.DeleteAsync($"{PartiesUrl}/{lonelyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.OwnerClient.GetAsync($"{PartiesUrl}/{lonelyId}")).StatusCode);
        Assert.DoesNotContain("Lonely", Names(await ListAsync(scenario.OwnerClient)));
        Assert.Equal(1L, await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from party.parties where company_id = $1 and id = $2 and is_deleted", scenario.CompanyId, lonelyId));

        var protectedParty = await scenario.OwnerClient.DeleteAsync($"{PartiesUrl}/{busyId}");
        Assert.Equal(HttpStatusCode.Conflict, protectedParty.StatusCode);
        var problem = await OwnerCompanyScenario.ReadJsonAsync(protectedParty);
        Assert.Equal(ErrorCodes.RecordInUse, problem.GetProperty("errorCode").GetString());
        Assert.Contains("acct.journal_lines", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task An_opening_balance_can_be_entered_with_the_party_and_it_feeds_the_derived_balance()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var partyId = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, PartiesUrl, PartyBody(
            "Vendor with opening", isCustomer: false, isVendor: true,
            openingBalance: new { sideCode = "CREDIT", amount = 12_000L, asOfDate = "2026-01-01" }))).GetProperty("id").GetGuid();

        var party = await GetPartyAsync(scenario.OwnerClient, partyId);
        Assert.Equal(-12_000, party.GetProperty("balanceAmount").GetInt64());          // we owe the vendor
        Assert.Equal("CREDIT", party.GetProperty("openingBalance").GetProperty("sideCode").GetString());
        Assert.Equal(12_000, party.GetProperty("openingBalance").GetProperty("amount").GetInt64());
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));

        var rowVersion = party.GetProperty("rowVersion").GetInt64();
        var change = await scenario.OwnerClient.PutAsJsonAsync($"{PartiesUrl}/{partyId}", PartyBody(
            "Vendor with opening", isCustomer: false, isVendor: true,
            openingBalance: new { sideCode = "DEBIT", amount = 3_000L, asOfDate = "2026-01-01" }, rowVersion: rowVersion));
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.Equal(3_000, (await GetPartyAsync(scenario.OwnerClient, partyId)).GetProperty("balanceAmount").GetInt64());
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));
    }

    [Fact]
    public async Task An_opening_balance_with_a_party_needs_the_opening_balance_permission()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var withoutRestriction = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Party.NewParty.Add, PermissionCodes.Party.NewParty.View);
        var body = PartyBody("Needs permission", openingBalance: new { sideCode = "DEBIT", amount = 100L, asOfDate = "2026-01-01" });

        var denied = await withoutRestriction.PostAsJsonAsync(PartiesUrl, body);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(0, (await ListAsync(scenario.OwnerClient, "?searchText=Needs permission")).GetProperty("totalCount").GetInt32());   // nothing was saved

        var withoutOpening = await withoutRestriction.PostAsJsonAsync(PartiesUrl, PartyBody("No opening balance"));
        Assert.Equal(HttpStatusCode.Created, withoutOpening.StatusCode);
    }

    [Fact]
    public async Task Each_party_action_needs_its_own_permission_but_the_lookup_is_open_to_every_company_user()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var partyId = await CreatePartyAsync(scenario.OwnerClient, "Walk in friend");
        var nothing = await scenario.SignInUserWithPermissionsAsync(RoleCodes.CashierSalesman, [scenario.FirstStoreId]);

        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.GetAsync(PartiesUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.GetAsync(SearchUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.GetAsync($"{PartiesUrl}/{partyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.PostAsJsonAsync(PartiesUrl, PartyBody("Nope"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.DeleteAsync($"{PartiesUrl}/{partyId}")).StatusCode);

        var lookup = await OwnerCompanyScenario.ReadJsonAsync(await nothing.GetAsync($"{LookupUrl}?partyTypeCode=CUSTOMER&searchText=friend"));
        Assert.Equal(["Walk in friend"], lookup.EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToArray());
        var walkIn = await OwnerCompanyScenario.ReadJsonAsync(await nothing.GetAsync($"{LookupUrl}?searchText=Cash Sale"));
        Assert.Equal(1, walkIn.GetArrayLength());   // every company starts with the Cash Sale customer

        var adder = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Party.NewParty.Add);
        Assert.Equal(HttpStatusCode.Created, (await adder.PostAsJsonAsync(PartiesUrl, PartyBody("Allowed"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await adder.GetAsync(PartiesUrl)).StatusCode);
    }

    [Fact]
    public async Task One_company_never_sees_or_changes_the_parties_of_another()
    {
        var companyA = await OwnerCompanyScenario.CreateAsync(environment);
        var companyB = await OwnerCompanyScenario.CreateAsync(environment);
        var partyOfA = await CreatePartyAsync(companyA.OwnerClient, "Customer of A only");

        Assert.DoesNotContain("Customer of A only", Names(await ListAsync(companyB.OwnerClient)));
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.GetAsync($"{PartiesUrl}/{partyOfA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.DeleteAsync($"{PartiesUrl}/{partyOfA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.PutAsJsonAsync($"{PartiesUrl}/{partyOfA}", PartyBody("Hijacked", rowVersion: 1))).StatusCode);
        var lookupOfB = await OwnerCompanyScenario.ReadJsonAsync(await companyB.OwnerClient.GetAsync($"{LookupUrl}?searchText=Customer of A only"));
        Assert.Equal(0, lookupOfB.GetArrayLength());
        Assert.Equal("Customer of A only", (await GetPartyAsync(companyA.OwnerClient, partyOfA)).GetProperty("name").GetString());
    }
}
