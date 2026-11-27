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

namespace EFootwear.Tests.Vendors;

/// <summary>New Vendor, View Vendors: validation, codes, search, deletion, opening balance, permissions and tenant isolation.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class VendorApiTests(ApiTestEnvironment environment)
{
    private static readonly string LookupUrl = TestScenarioBuilder.Url(ApiRoutes.Vendors.Root, ApiRoutes.Vendors.Lookup);
    private static readonly string CitiesUrl = TestScenarioBuilder.Url(ApiRoutes.MasterData.Cities);

    private static async Task<JsonElement> ListAsync(HttpClient client, string query = "") =>
        await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(VendorsUrl + query));

    private static string[] Names(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!).ToArray();

    [Fact]
    public async Task A_vendor_is_created_with_a_generated_code_and_all_its_fields_come_back()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var cityId = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, CitiesUrl, new { name = "Sahiwal" })).GetProperty("id").GetGuid();

        var vendorId = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, VendorsUrl, VendorBody(
            "  Muhammad   Ali ", phone: "0300 1234567", email: "ali@example.test", birthDate: "1990-05-17", cityId: cityId, creditLimit: 50_000))).GetProperty("id").GetGuid();

        var vendor = await GetVendorAsync(scenario.OwnerClient, vendorId);
        Assert.Matches(@"^103\d{5}$", vendor.GetProperty("code").GetString()!);
        Assert.Equal("Muhammad Ali", vendor.GetProperty("name").GetString());
        Assert.Equal("0300 1234567", vendor.GetProperty("phone").GetString());
        Assert.Equal("ali@example.test", vendor.GetProperty("email").GetString());
        Assert.Equal("1990-05-17", vendor.GetProperty("birthDate").GetString());
        Assert.Equal("Sahiwal", vendor.GetProperty("cityName").GetString());
        Assert.Equal(50_000, vendor.GetProperty("creditLimit").GetInt64());
        Assert.Equal("ACTIVE", vendor.GetProperty("statusCode").GetString());
        Assert.Equal(0, vendor.GetProperty("balanceAmount").GetInt64());
        Assert.False(vendor.TryGetProperty("cnic", out _));          // the removed fields are gone
        Assert.False(vendor.TryGetProperty("fatherName", out _));
    }

    [Fact]
    public async Task Vendor_codes_are_generated_one_after_another()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var first = await GetVendorAsync(scenario.OwnerClient, await CreateVendorAsync(scenario.OwnerClient, "First"));
        var second = await GetVendorAsync(scenario.OwnerClient, await CreateVendorAsync(scenario.OwnerClient, "Second"));

        var firstNumber = long.Parse(first.GetProperty("code").GetString()!);
        var secondNumber = long.Parse(second.GetProperty("code").GetString()!);
        Assert.Equal(firstNumber + 1, secondNumber);
    }

    [Theory]
    [InlineData("noName")]
    [InlineData("noPhone")]
    [InlineData("badEmail")]
    [InlineData("badPhone")]
    [InlineData("futureBirthDate")]
    [InlineData("negativeCreditLimit")]
    [InlineData("badStatus")]
    [InlineData("unknownCity")]
    public async Task Invalid_vendors_are_refused_with_a_validation_error(string problem)
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        object body = problem switch
        {
            "noName" => VendorBody("   "),
            "noPhone" => VendorBody("No Phone", phone: null),
            "badEmail" => VendorBody("Bad Email", email: "not-an-email"),
            "badPhone" => VendorBody("Bad Phone", phone: "call me"),
            "futureBirthDate" => VendorBody("Born Tomorrow", birthDate: DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd")),
            "negativeCreditLimit" => VendorBody("Negative", creditLimit: -1),
            "badStatus" => VendorBody("Bad Status", statusCode: "SLEEPING"),
            _ => VendorBody("Unknown City", cityId: Guid.NewGuid()),
        };

        var response = await scenario.OwnerClient.PostAsJsonAsync(VendorsUrl, body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, (await OwnerCompanyScenario.ReadJsonAsync(response)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task View_vendors_filters_by_city_and_status_and_search_finds_by_name_code_phone_and_email()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var multan = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, CitiesUrl, new { name = "Multan" })).GetProperty("id").GetGuid();
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, VendorsUrl, VendorBody("Bata Distributors", cityId: multan, phone: "0301 5550001"));
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, VendorsUrl, VendorBody("Retail Vendor", phone: "0302 5550002", email: "retail@example.test"));
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, VendorsUrl, VendorBody("Sleeping Vendor", statusCode: "INACTIVE"));

        Assert.Equal(new[] { "Bata Distributors" }, Names(await ListAsync(scenario.OwnerClient, $"?cityId={multan}")));
        Assert.Equal(new[] { "Sleeping Vendor" }, Names(await ListAsync(scenario.OwnerClient, "?statusCode=INACTIVE")));
        Assert.Equal(1, (await ListAsync(scenario.OwnerClient, "?searchText=0302")).GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { "Retail Vendor" }, Names(await ListAsync(scenario.OwnerClient, "?searchText=retail@example")));
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.GetAsync($"{VendorsUrl}?statusCode=ANIMAL")).StatusCode);

        var sorted = Names(await ListAsync(scenario.OwnerClient, "?sortField=name&sortDirection=Descending"));
        Assert.Equal(sorted.OrderByDescending(name => name, StringComparer.Ordinal).ToArray(), sorted);

        // the free text search of the same list
        var viaSearch = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{VendorsUrl}?searchText=bata"));
        Assert.Equal(new[] { "Bata Distributors" }, Names(viaSearch));
    }

    [Fact]
    public async Task A_vendor_is_edited_with_its_row_version_and_a_stale_version_is_refused()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var vendorId = await CreateVendorAsync(scenario.OwnerClient, "Before");
        var loaded = await GetVendorAsync(scenario.OwnerClient, vendorId);
        var rowVersion = loaded.GetProperty("rowVersion").GetInt64();

        var edit = await scenario.OwnerClient.PutAsJsonAsync($"{VendorsUrl}/{vendorId}", VendorBody("After", email: "after@example.test", rowVersion: rowVersion));
        Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);
        var reloaded = await GetVendorAsync(scenario.OwnerClient, vendorId);
        Assert.Equal("After", reloaded.GetProperty("name").GetString());
        Assert.Equal("after@example.test", reloaded.GetProperty("email").GetString());
        Assert.Equal(loaded.GetProperty("code").GetString(), reloaded.GetProperty("code").GetString());   // the code never changes

        var stale = await scenario.OwnerClient.PutAsJsonAsync($"{VendorsUrl}/{vendorId}", VendorBody("Late edit", rowVersion: rowVersion));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(ErrorCodes.ConcurrentUpdate, (await OwnerCompanyScenario.ReadJsonAsync(stale)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_vendor_without_history_is_deleted_softly_and_a_vendor_with_accounting_history_is_protected()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var lonelyId = await CreateVendorAsync(scenario.OwnerClient, "Lonely");
        var busyId = await CreateVendorAsync(scenario.OwnerClient, "Busy");
        await PostVendorLedgerEntryAsync(environment, scenario.CompanyId, busyId, new DateOnly(2026, 3, 1), 1_000, 0);

        Assert.Equal(HttpStatusCode.NoContent, (await scenario.OwnerClient.DeleteAsync($"{VendorsUrl}/{lonelyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.OwnerClient.GetAsync($"{VendorsUrl}/{lonelyId}")).StatusCode);
        Assert.DoesNotContain("Lonely", Names(await ListAsync(scenario.OwnerClient)));
        Assert.Equal(1L, await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from contacts.vendors where company_id = $1 and id = $2 and is_deleted", scenario.CompanyId, lonelyId));

        var protectedVendor = await scenario.OwnerClient.DeleteAsync($"{VendorsUrl}/{busyId}");
        Assert.Equal(HttpStatusCode.Conflict, protectedVendor.StatusCode);
        var problem = await OwnerCompanyScenario.ReadJsonAsync(protectedVendor);
        Assert.Equal(ErrorCodes.RecordInUse, problem.GetProperty("errorCode").GetString());
        Assert.Contains("acct.journal_lines", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task An_opening_balance_can_be_entered_with_the_vendor_and_it_feeds_the_derived_balance()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var vendorId = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, VendorsUrl, VendorBody(
            "Vendor with opening", openingBalance: new { sideCode = "CREDIT", amount = 12_000L, asOfDate = "2026-01-01" }))).GetProperty("id").GetGuid();

        var vendor = await GetVendorAsync(scenario.OwnerClient, vendorId);
        Assert.Equal(-12_000, vendor.GetProperty("balanceAmount").GetInt64());          // credit: we owe the vendor
        Assert.Equal("CREDIT", vendor.GetProperty("openingBalance").GetProperty("sideCode").GetString());
        Assert.Equal(12_000, vendor.GetProperty("openingBalance").GetProperty("amount").GetInt64());
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));

        var rowVersion = vendor.GetProperty("rowVersion").GetInt64();
        var change = await scenario.OwnerClient.PutAsJsonAsync($"{VendorsUrl}/{vendorId}", VendorBody(
            "Vendor with opening", openingBalance: new { sideCode = "DEBIT", amount = 3_000L, asOfDate = "2026-01-01" }, rowVersion: rowVersion));
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.Equal(3_000, (await GetVendorAsync(scenario.OwnerClient, vendorId)).GetProperty("balanceAmount").GetInt64());
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));
    }

    [Fact]
    public async Task An_opening_balance_with_a_vendor_needs_the_opening_balance_permission()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var withoutRestriction = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Vendors.NewVendor.Add, PermissionCodes.Vendors.NewVendor.View);
        var body = VendorBody("Needs permission", openingBalance: new { sideCode = "DEBIT", amount = 100L, asOfDate = "2026-01-01" });

        var denied = await withoutRestriction.PostAsJsonAsync(VendorsUrl, body);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(0, (await ListAsync(scenario.OwnerClient, "?searchText=Needs permission")).GetProperty("totalCount").GetInt32());   // nothing was saved

        var withoutOpening = await withoutRestriction.PostAsJsonAsync(VendorsUrl, VendorBody("No opening balance"));
        Assert.Equal(HttpStatusCode.Created, withoutOpening.StatusCode);
    }

    [Fact]
    public async Task Each_vendor_action_needs_its_own_permission_but_the_lookup_is_open_to_every_company_user()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var vendorId = await CreateVendorAsync(scenario.OwnerClient, "Walk in friend");
        var nothing = await scenario.SignInUserWithPermissionsAsync(RoleCodes.CashierSalesman, [scenario.FirstStoreId]);

        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.GetAsync(VendorsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.GetAsync($"{VendorsUrl}/{vendorId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.PostAsJsonAsync(VendorsUrl, VendorBody("Nope"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.DeleteAsync($"{VendorsUrl}/{vendorId}")).StatusCode);

        var lookup = await OwnerCompanyScenario.ReadJsonAsync(await nothing.GetAsync($"{LookupUrl}?searchText=friend"));
        Assert.Equal(new[] { "Walk in friend" }, lookup.EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToArray());

        var adder = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Vendors.NewVendor.Add);
        Assert.Equal(HttpStatusCode.Created, (await adder.PostAsJsonAsync(VendorsUrl, VendorBody("Allowed"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await adder.GetAsync(VendorsUrl)).StatusCode);

    }

    [Fact]
    public async Task One_company_never_sees_or_changes_the_vendors_of_another()
    {
        var companyA = await OwnerCompanyScenario.CreateAsync(environment);
        var companyB = await OwnerCompanyScenario.CreateAsync(environment);
        var vendorOfA = await CreateVendorAsync(companyA.OwnerClient, "Vendor of A only");

        Assert.DoesNotContain("Vendor of A only", Names(await ListAsync(companyB.OwnerClient)));
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.GetAsync($"{VendorsUrl}/{vendorOfA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.DeleteAsync($"{VendorsUrl}/{vendorOfA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.PutAsJsonAsync($"{VendorsUrl}/{vendorOfA}", VendorBody("Hijacked", rowVersion: 1))).StatusCode);
        var lookupOfB = await OwnerCompanyScenario.ReadJsonAsync(await companyB.OwnerClient.GetAsync($"{LookupUrl}?searchText=Vendor of A only"));
        Assert.Equal(0, lookupOfB.GetArrayLength());
        Assert.Equal("Vendor of A only", (await GetVendorAsync(companyA.OwnerClient, vendorOfA)).GetProperty("name").GetString());
    }
}
