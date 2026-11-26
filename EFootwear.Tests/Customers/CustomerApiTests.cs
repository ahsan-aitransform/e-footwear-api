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

namespace EFootwear.Tests.Customers;

/// <summary>New Customer, View Customers and Search Customer: validation, codes, search, deletion, opening balance, permissions and tenant isolation.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class CustomerApiTests(ApiTestEnvironment environment)
{
    private static readonly string SearchUrl = TestScenarioBuilder.Url(ApiRoutes.Customers.Root, ApiRoutes.Customers.Search);
    private static readonly string LookupUrl = TestScenarioBuilder.Url(ApiRoutes.Customers.Root, ApiRoutes.Customers.Lookup);
    private static readonly string CitiesUrl = TestScenarioBuilder.Url(ApiRoutes.MasterData.Cities);

    private static async Task<JsonElement> ListAsync(HttpClient client, string query = "") =>
        await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(CustomersUrl + query));

    private static string[] Names(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!).ToArray();

    [Fact]
    public async Task A_customer_is_created_with_a_generated_code_and_all_its_fields_come_back()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var cityId = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, CitiesUrl, new { name = "Sahiwal" })).GetProperty("id").GetGuid();

        var customerId = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, CustomersUrl, CustomerBody(
            "  Muhammad   Ali ", phone: "0300 1234567", email: "ali@example.test", birthDate: "1990-05-17", cityId: cityId))).GetProperty("id").GetGuid();

        var customer = await GetCustomerAsync(scenario.OwnerClient, customerId);
        Assert.Matches(@"^103\d{5}$", customer.GetProperty("code").GetString()!);
        Assert.Equal("Muhammad Ali", customer.GetProperty("name").GetString());
        Assert.Equal("0300 1234567", customer.GetProperty("phone").GetString());
        Assert.Equal("ali@example.test", customer.GetProperty("email").GetString());
        Assert.Equal("1990-05-17", customer.GetProperty("birthDate").GetString());
        Assert.Equal("Sahiwal", customer.GetProperty("cityName").GetString());
        Assert.Equal("ACTIVE", customer.GetProperty("statusCode").GetString());
        Assert.Equal(0, customer.GetProperty("balanceAmount").GetInt64());
        Assert.Equal(JsonValueKind.Null, customer.GetProperty("cnic").ValueKind);
        Assert.Equal("REGULAR", customer.GetProperty("customerTypeCode").GetString());
        Assert.False(customer.TryGetProperty("fatherName", out _));    // the removed field is gone
    }

    [Fact]
    public async Task The_cnic_is_stored_with_dashes_and_an_edit_without_a_customer_type_keeps_it()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var customerId = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, CustomersUrl, CustomerBody(
            "Special One", cnic: "3520212345671", customerTypeCode: "SPECIAL"))).GetProperty("id").GetGuid();

        var created = await GetCustomerAsync(scenario.OwnerClient, customerId);
        Assert.Equal("35202-1234567-1", created.GetProperty("cnic").GetString());
        Assert.Equal("SPECIAL", created.GetProperty("customerTypeCode").GetString());

        var edit = await scenario.OwnerClient.PutAsJsonAsync($"{CustomersUrl}/{customerId}", CustomerBody("Special One", rowVersion: created.GetProperty("rowVersion").GetInt64()));
        Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);
        var edited = await GetCustomerAsync(scenario.OwnerClient, customerId);
        Assert.Equal(JsonValueKind.Null, edited.GetProperty("cnic").ValueKind);
        Assert.Equal("SPECIAL", edited.GetProperty("customerTypeCode").GetString());
    }

    [Fact]
    public async Task Customer_codes_are_generated_one_after_another()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var first = await GetCustomerAsync(scenario.OwnerClient, await CreateCustomerAsync(scenario.OwnerClient, "First"));
        var second = await GetCustomerAsync(scenario.OwnerClient, await CreateCustomerAsync(scenario.OwnerClient, "Second"));

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
    [InlineData("badStatus")]
    [InlineData("unknownCity")]
    [InlineData("badCnic")]
    [InlineData("badCustomerType")]
    public async Task Invalid_customers_are_refused_with_a_validation_error(string problem)
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        object body = problem switch
        {
            "noName" => CustomerBody("   "),
            "noPhone" => CustomerBody("No Phone", phone: null),
            "badEmail" => CustomerBody("Bad Email", email: "not-an-email"),
            "badPhone" => CustomerBody("Bad Phone", phone: "call me"),
            "futureBirthDate" => CustomerBody("Born Tomorrow", birthDate: DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd")),
            "badStatus" => CustomerBody("Bad Status", statusCode: "SLEEPING"),
            "badCnic" => CustomerBody("Bad Cnic", cnic: "12345"),
            "badCustomerType" => CustomerBody("Bad Type", customerTypeCode: "GOLD"),
            _ => CustomerBody("Unknown City", cityId: Guid.NewGuid()),
        };

        var response = await scenario.OwnerClient.PostAsJsonAsync(CustomersUrl, body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, (await OwnerCompanyScenario.ReadJsonAsync(response)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task View_customers_filters_by_city_and_status_and_search_finds_by_name_code_phone_and_email()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var multan = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, CitiesUrl, new { name = "Multan" })).GetProperty("id").GetGuid();
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, CustomersUrl, CustomerBody("Bata Distributors", cityId: multan, phone: "0301 5550001"));
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, CustomersUrl, CustomerBody("Retail Customer", phone: "0302 5550002", email: "retail@example.test"));
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, CustomersUrl, CustomerBody("Sleeping Customer", statusCode: "INACTIVE"));

        Assert.Equal(new[] { "Bata Distributors" }, Names(await ListAsync(scenario.OwnerClient, $"?cityId={multan}")));
        Assert.Equal(new[] { "Sleeping Customer" }, Names(await ListAsync(scenario.OwnerClient, "?statusCode=INACTIVE")));
        Assert.Equal(1, (await ListAsync(scenario.OwnerClient, "?searchText=0302")).GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { "Retail Customer" }, Names(await ListAsync(scenario.OwnerClient, "?searchText=retail@example")));
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.GetAsync($"{CustomersUrl}?statusCode=ANIMAL")).StatusCode);

        var sorted = Names(await ListAsync(scenario.OwnerClient, "?sortField=name&sortDirection=Descending"));
        Assert.Equal(sorted.OrderByDescending(name => name, StringComparer.Ordinal).ToArray(), sorted);

        // Search Customer is its own screen with its own endpoint
        var viaSearch = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{SearchUrl}?searchText=bata"));
        Assert.Equal(new[] { "Bata Distributors" }, Names(viaSearch));
    }

    [Fact]
    public async Task A_customer_is_edited_with_its_row_version_and_a_stale_version_is_refused()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var customerId = await CreateCustomerAsync(scenario.OwnerClient, "Before");
        var loaded = await GetCustomerAsync(scenario.OwnerClient, customerId);
        var rowVersion = loaded.GetProperty("rowVersion").GetInt64();

        var edit = await scenario.OwnerClient.PutAsJsonAsync($"{CustomersUrl}/{customerId}", CustomerBody("After", email: "after@example.test", rowVersion: rowVersion));
        Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);
        var reloaded = await GetCustomerAsync(scenario.OwnerClient, customerId);
        Assert.Equal("After", reloaded.GetProperty("name").GetString());
        Assert.Equal("after@example.test", reloaded.GetProperty("email").GetString());
        Assert.Equal(loaded.GetProperty("code").GetString(), reloaded.GetProperty("code").GetString());   // the code never changes

        var stale = await scenario.OwnerClient.PutAsJsonAsync($"{CustomersUrl}/{customerId}", CustomerBody("Late edit", rowVersion: rowVersion));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(ErrorCodes.ConcurrentUpdate, (await OwnerCompanyScenario.ReadJsonAsync(stale)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_customer_without_history_is_deleted_softly_and_a_customer_with_accounting_history_is_protected()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var lonelyId = await CreateCustomerAsync(scenario.OwnerClient, "Lonely");
        var busyId = await CreateCustomerAsync(scenario.OwnerClient, "Busy");
        await PostCustomerLedgerEntryAsync(environment, scenario.CompanyId, busyId, new DateOnly(2026, 3, 1), 1_000, 0);

        Assert.Equal(HttpStatusCode.NoContent, (await scenario.OwnerClient.DeleteAsync($"{CustomersUrl}/{lonelyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.OwnerClient.GetAsync($"{CustomersUrl}/{lonelyId}")).StatusCode);
        Assert.DoesNotContain("Lonely", Names(await ListAsync(scenario.OwnerClient)));
        Assert.Equal(1L, await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from contacts.customers where company_id = $1 and id = $2 and is_deleted", scenario.CompanyId, lonelyId));

        var protectedCustomer = await scenario.OwnerClient.DeleteAsync($"{CustomersUrl}/{busyId}");
        Assert.Equal(HttpStatusCode.Conflict, protectedCustomer.StatusCode);
        var problem = await OwnerCompanyScenario.ReadJsonAsync(protectedCustomer);
        Assert.Equal(ErrorCodes.RecordInUse, problem.GetProperty("errorCode").GetString());
        Assert.Contains("acct.journal_lines", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task An_opening_balance_can_be_entered_with_the_customer_and_it_feeds_the_derived_balance()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var customerId = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, CustomersUrl, CustomerBody(
            "Customer with opening", openingBalance: new { sideCode = "CREDIT", amount = 12_000L, asOfDate = "2026-01-01" }))).GetProperty("id").GetGuid();

        var customer = await GetCustomerAsync(scenario.OwnerClient, customerId);
        Assert.Equal(-12_000, customer.GetProperty("balanceAmount").GetInt64());          // credit: we owe the customer
        Assert.Equal("CREDIT", customer.GetProperty("openingBalance").GetProperty("sideCode").GetString());
        Assert.Equal(12_000, customer.GetProperty("openingBalance").GetProperty("amount").GetInt64());
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));

        var rowVersion = customer.GetProperty("rowVersion").GetInt64();
        var change = await scenario.OwnerClient.PutAsJsonAsync($"{CustomersUrl}/{customerId}", CustomerBody(
            "Customer with opening", openingBalance: new { sideCode = "DEBIT", amount = 3_000L, asOfDate = "2026-01-01" }, rowVersion: rowVersion));
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.Equal(3_000, (await GetCustomerAsync(scenario.OwnerClient, customerId)).GetProperty("balanceAmount").GetInt64());
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));
    }

    [Fact]
    public async Task An_opening_balance_with_a_customer_needs_the_opening_balance_permission()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var withoutRestriction = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Customers.NewCustomer.Add, PermissionCodes.Customers.NewCustomer.View);
        var body = CustomerBody("Needs permission", openingBalance: new { sideCode = "DEBIT", amount = 100L, asOfDate = "2026-01-01" });

        var denied = await withoutRestriction.PostAsJsonAsync(CustomersUrl, body);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(0, (await ListAsync(scenario.OwnerClient, "?searchText=Needs permission")).GetProperty("totalCount").GetInt32());   // nothing was saved

        var withoutOpening = await withoutRestriction.PostAsJsonAsync(CustomersUrl, CustomerBody("No opening balance"));
        Assert.Equal(HttpStatusCode.Created, withoutOpening.StatusCode);
    }

    [Fact]
    public async Task Each_customer_action_needs_its_own_permission_but_the_lookup_is_open_to_every_company_user()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var customerId = await CreateCustomerAsync(scenario.OwnerClient, "Walk in friend");
        var nothing = await scenario.SignInUserWithPermissionsAsync(RoleCodes.CashierSalesman, [scenario.FirstStoreId]);

        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.GetAsync(CustomersUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.GetAsync(SearchUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.GetAsync($"{CustomersUrl}/{customerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.PostAsJsonAsync(CustomersUrl, CustomerBody("Nope"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.DeleteAsync($"{CustomersUrl}/{customerId}")).StatusCode);

        var lookup = await OwnerCompanyScenario.ReadJsonAsync(await nothing.GetAsync($"{LookupUrl}?searchText=friend"));
        Assert.Equal(new[] { "Walk in friend" }, lookup.EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToArray());

        var adder = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Customers.NewCustomer.Add);
        Assert.Equal(HttpStatusCode.Created, (await adder.PostAsJsonAsync(CustomersUrl, CustomerBody("Allowed"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await adder.GetAsync(CustomersUrl)).StatusCode);

        // every company starts with the Cash Sale customer, and it is found by the lookup
        var walkIn = await OwnerCompanyScenario.ReadJsonAsync(await nothing.GetAsync($"{LookupUrl}?searchText=Cash Sale"));
        Assert.Equal(1, walkIn.GetArrayLength());
    }

    [Fact]
    public async Task The_walk_in_customer_belongs_to_the_system_and_cannot_be_edited_or_deleted()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var lookup = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{LookupUrl}?searchText=Cash Sale"));
        var walkInId = lookup[0].GetProperty("id").GetGuid();
        var walkIn = await GetCustomerAsync(scenario.OwnerClient, walkInId);
        Assert.Equal("WALKIN", walkIn.GetProperty("code").GetString());

        var edit = await scenario.OwnerClient.PutAsJsonAsync($"{CustomersUrl}/{walkInId}", CustomerBody("Renamed", rowVersion: walkIn.GetProperty("rowVersion").GetInt64()));
        Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.DeleteAsync($"{CustomersUrl}/{walkInId}")).StatusCode);
    }

    [Fact]
    public async Task One_company_never_sees_or_changes_the_customers_of_another()
    {
        var companyA = await OwnerCompanyScenario.CreateAsync(environment);
        var companyB = await OwnerCompanyScenario.CreateAsync(environment);
        var customerOfA = await CreateCustomerAsync(companyA.OwnerClient, "Customer of A only");

        Assert.DoesNotContain("Customer of A only", Names(await ListAsync(companyB.OwnerClient)));
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.GetAsync($"{CustomersUrl}/{customerOfA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.DeleteAsync($"{CustomersUrl}/{customerOfA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.PutAsJsonAsync($"{CustomersUrl}/{customerOfA}", CustomerBody("Hijacked", rowVersion: 1))).StatusCode);
        var lookupOfB = await OwnerCompanyScenario.ReadJsonAsync(await companyB.OwnerClient.GetAsync($"{LookupUrl}?searchText=Customer of A only"));
        Assert.Equal(0, lookupOfB.GetArrayLength());
        Assert.Equal("Customer of A only", (await GetCustomerAsync(companyA.OwnerClient, customerOfA)).GetProperty("name").GetString());
    }
}
