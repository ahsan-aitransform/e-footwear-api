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

using EFootwear.Tests.Contacts;

namespace EFootwear.Tests.CashManagement;

/// <summary>Customer promises (docx 3.5): numbering per store, totals, editing, cancelling, store access and isolation.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class PromiseApiTests(ApiTestEnvironment environment)
{
    private static readonly string PromisesUrl = TestScenarioBuilder.Url(ApiRoutes.Promises.Root);

    private static string BalanceUrl(Guid customerId) => $"{PromisesUrl}/customers/{customerId}/balance";

    private static object NewPromise(Guid storeId, Guid customerId, long amount, string dueDate = "2026-11-27", string promiseDate = "2026-11-20", string? note = null) =>
        new { storeId, customerId, promiseDate, dueDate, promisedAmount = amount, note };

    private static async Task<Guid> CreatePromiseAsync(HttpClient client, object body) =>
        (await OwnerCompanyScenario.PostJsonAsync(client, PromisesUrl, body)).GetProperty("id").GetGuid();

    private static async Task<JsonElement> ListAsync(HttpClient client, string query = "") =>
        await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(PromisesUrl + query));

    [Fact]
    public async Task Promises_are_numbered_per_store_and_listed_with_totals()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        var customerId = await CreateCustomerAsync(scenario.OwnerClient, "Promise Customer");

        var first = await CreatePromiseAsync(scenario.OwnerClient, NewPromise(scenario.FirstStoreId, customerId, 5_000, note: "After salary"));
        await CreatePromiseAsync(scenario.OwnerClient, NewPromise(scenario.FirstStoreId, customerId, 3_000, dueDate: "2026-11-25"));
        await CreatePromiseAsync(scenario.OwnerClient, NewPromise(secondStoreId, customerId, 700));

        var list = await ListAsync(scenario.OwnerClient);
        Assert.Equal(3, list.GetProperty("promises").GetProperty("totalCount").GetInt32());
        Assert.Equal(8_700, list.GetProperty("totalPromisedAmount").GetInt64());
        Assert.Equal(0, list.GetProperty("totalPaidAmount").GetInt64());
        Assert.Equal(8_700, list.GetProperty("totalRemainingAmount").GetInt64());

        var firstRow = list.GetProperty("promises").GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == first);
        Assert.Equal(1, firstRow.GetProperty("promiseNumber").GetInt64());
        Assert.Equal("Promise Customer", firstRow.GetProperty("customerName").GetString());
        Assert.Equal("After salary", firstRow.GetProperty("note").GetString());
        Assert.Equal(5_000, firstRow.GetProperty("remainingAmount").GetInt64());
        Assert.Equal("OPEN", firstRow.GetProperty("statusCode").GetString());

        var numbersOfFirstStore = (await ListAsync(scenario.OwnerClient, $"?storeId={scenario.FirstStoreId}")).GetProperty("promises").GetProperty("items")
            .EnumerateArray().Select(item => item.GetProperty("promiseNumber").GetInt64()).Order().ToArray();
        Assert.Equal([1L, 2L], numbersOfFirstStore);
        var numberOfSecondStore = (await ListAsync(scenario.OwnerClient, $"?storeId={secondStoreId}")).GetProperty("promises").GetProperty("items")[0].GetProperty("promiseNumber").GetInt64();
        Assert.Equal(1, numberOfSecondStore);   // every store counts from 1
    }

    [Fact]
    public async Task The_list_filters_by_due_date_and_searches_by_customer_and_description()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var ali = await CreateCustomerAsync(scenario.OwnerClient, "Ali Khan");
        var sara = await CreateCustomerAsync(scenario.OwnerClient, "Sara Bibi");
        await CreatePromiseAsync(scenario.OwnerClient, NewPromise(scenario.FirstStoreId, ali, 1_000, dueDate: "2026-11-21", note: "shoes"));
        await CreatePromiseAsync(scenario.OwnerClient, NewPromise(scenario.FirstStoreId, sara, 2_000, dueDate: "2026-11-28", note: "bag"));

        var thisWeek = await ListAsync(scenario.OwnerClient, "?from=2026-11-20&to=2026-11-26");
        Assert.Equal(1, thisWeek.GetProperty("promises").GetProperty("totalCount").GetInt32());
        Assert.Equal(1_000, thisWeek.GetProperty("totalPromisedAmount").GetInt64());
        Assert.Equal(1, (await ListAsync(scenario.OwnerClient, "?searchText=sara")).GetProperty("promises").GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await ListAsync(scenario.OwnerClient, "?searchText=shoes")).GetProperty("promises").GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.GetAsync($"{PromisesUrl}?from=2026-12-01&to=2026-11-01")).StatusCode);
    }

    [Fact]
    public async Task A_promise_needs_a_positive_amount_sensible_dates_and_an_active_customer()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var customerId = await CreateCustomerAsync(scenario.OwnerClient, "Valid Customer");
        var vendorOnlyId = await ContactTestHelpers.CreateVendorAsync(scenario.OwnerClient, "Vendor Only");

        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PostAsJsonAsync(PromisesUrl, NewPromise(scenario.FirstStoreId, customerId, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PostAsJsonAsync(PromisesUrl, NewPromise(scenario.FirstStoreId, customerId, 100, dueDate: "2026-11-01"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PostAsJsonAsync(PromisesUrl, NewPromise(scenario.FirstStoreId, vendorOnlyId, 100))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PostAsJsonAsync(PromisesUrl, NewPromise(scenario.FirstStoreId, Guid.NewGuid(), 100))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PostAsJsonAsync(PromisesUrl, NewPromise(Guid.Empty, customerId, 100))).StatusCode);   // the head office is not a selling store
    }

    [Fact]
    public async Task The_customer_balance_field_shows_what_the_customer_owes()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var customerId = await CreateCustomerAsync(scenario.OwnerClient, "Debtor");
        await PostCustomerLedgerEntryAsync(environment, scenario.CompanyId, customerId, new DateOnly(2026, 3, 1), 4_200, 0);

        var balance = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(BalanceUrl(customerId)));
        Assert.Equal(4_200, balance.GetProperty("balanceAmount").GetInt64());
        Assert.Equal("Debtor", balance.GetProperty("customerName").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.OwnerClient.GetAsync(BalanceUrl(Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task An_open_promise_can_be_edited_and_a_stale_row_version_is_refused()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var customerId = await CreateCustomerAsync(scenario.OwnerClient, "Editor");
        var promiseId = await CreatePromiseAsync(scenario.OwnerClient, NewPromise(scenario.FirstStoreId, customerId, 1_000));
        var rowVersion = (await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{PromisesUrl}/{promiseId}"))).GetProperty("rowVersion").GetInt64();

        var edit = await scenario.OwnerClient.PutAsJsonAsync($"{PromisesUrl}/{promiseId}", new { dueDate = "2026-12-05", promisedAmount = 1_500L, note = "Moved", rowVersion });
        Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);
        var after = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{PromisesUrl}/{promiseId}"));
        Assert.Equal(1_500, after.GetProperty("promisedAmount").GetInt64());
        Assert.Equal("2026-12-05", after.GetProperty("dueDate").GetString());

        var stale = await scenario.OwnerClient.PutAsJsonAsync($"{PromisesUrl}/{promiseId}", new { dueDate = "2026-12-06", promisedAmount = 2_000L, note = "Late", rowVersion });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(ErrorCodes.ConcurrentUpdate, (await OwnerCompanyScenario.ReadJsonAsync(stale)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task The_amount_cannot_be_lowered_below_what_was_paid()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var customerId = await CreateCustomerAsync(scenario.OwnerClient, "Part payer");
        var promiseId = await CreatePromiseAsync(scenario.OwnerClient, NewPromise(scenario.FirstStoreId, customerId, 1_000));
        await environment.Database.ExecuteAsOwnerAsync(
            "update cash.promises set paid_amount = 600, status = 'PARTIAL' where company_id = $1 and id = $2", scenario.CompanyId, promiseId);
        var rowVersion = (await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{PromisesUrl}/{promiseId}"))).GetProperty("rowVersion").GetInt64();

        var tooLow = await scenario.OwnerClient.PutAsJsonAsync($"{PromisesUrl}/{promiseId}", new { dueDate = "2026-12-05", promisedAmount = 500L, note = (string?)null, rowVersion });
        Assert.Equal(HttpStatusCode.BadRequest, tooLow.StatusCode);
        var fine = await scenario.OwnerClient.PutAsJsonAsync($"{PromisesUrl}/{promiseId}", new { dueDate = "2026-12-05", promisedAmount = 800L, note = (string?)null, rowVersion });
        Assert.Equal(HttpStatusCode.NoContent, fine.StatusCode);
        var row = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{PromisesUrl}/{promiseId}"));
        Assert.Equal(200, row.GetProperty("remainingAmount").GetInt64());
    }

    [Fact]
    public async Task Delete_cancels_a_promise_which_then_stays_in_the_history_but_leaves_the_totals()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var customerId = await CreateCustomerAsync(scenario.OwnerClient, "Canceller");
        var kept = await CreatePromiseAsync(scenario.OwnerClient, NewPromise(scenario.FirstStoreId, customerId, 400));
        var cancelled = await CreatePromiseAsync(scenario.OwnerClient, NewPromise(scenario.FirstStoreId, customerId, 900));
        var rowVersion = (await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{PromisesUrl}/{cancelled}"))).GetProperty("rowVersion").GetInt64();

        Assert.Equal(HttpStatusCode.NoContent, (await scenario.OwnerClient.DeleteAsync($"{PromisesUrl}/{cancelled}?rowVersion={rowVersion}")).StatusCode);

        var row = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{PromisesUrl}/{cancelled}"));
        Assert.Equal("CANCELLED", row.GetProperty("statusCode").GetString());
        var list = await ListAsync(scenario.OwnerClient);
        Assert.Equal(2, list.GetProperty("promises").GetProperty("totalCount").GetInt32());   // still listed
        Assert.Equal(400, list.GetProperty("totalPromisedAmount").GetInt64());                    // but not counted
        Assert.NotEqual(Guid.Empty, kept);

        var newVersion = row.GetProperty("rowVersion").GetInt64();
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.OwnerClient.DeleteAsync($"{PromisesUrl}/{cancelled}?rowVersion={newVersion}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.OwnerClient.PutAsJsonAsync($"{PromisesUrl}/{cancelled}",
            new { dueDate = "2026-12-05", promisedAmount = 900L, note = (string?)null, rowVersion = newVersion })).StatusCode);
        Assert.Equal(1L, await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from cash.promises where company_id = $1 and id = $2", scenario.CompanyId, cancelled));   // never removed
    }

    [Fact]
    public async Task A_store_user_can_only_work_with_the_promises_of_the_stores_they_are_assigned_to()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        var customerId = await CreateCustomerAsync(scenario.OwnerClient, "Two store customer");
        var promiseOfSecondStore = await CreatePromiseAsync(scenario.OwnerClient, NewPromise(secondStoreId, customerId, 100));
        var manager = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Customers.Promise.View, PermissionCodes.Customers.Promise.Add, PermissionCodes.Customers.Promise.Edit);

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync(PromisesUrl, NewPromise(secondStoreId, customerId, 50))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync($"{PromisesUrl}?storeId={secondStoreId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"{PromisesUrl}/{promiseOfSecondStore}")).StatusCode);   // the row is hidden by row-level security
        Assert.Equal(HttpStatusCode.Created, (await manager.PostAsJsonAsync(PromisesUrl, NewPromise(scenario.FirstStoreId, customerId, 50))).StatusCode);

        var visible = await ListAsync(manager);
        Assert.Equal(1, visible.GetProperty("promises").GetProperty("totalCount").GetInt32());   // only the first store's promise
    }

    [Fact]
    public async Task Each_promise_action_needs_its_own_permission()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var customerId = await CreateCustomerAsync(scenario.OwnerClient, "Guarded");
        var promiseId = await CreatePromiseAsync(scenario.OwnerClient, NewPromise(scenario.FirstStoreId, customerId, 100));
        var nothing = await scenario.SignInUserWithPermissionsAsync(RoleCodes.CashierSalesman, [scenario.FirstStoreId]);

        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.GetAsync(PromisesUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.GetAsync(BalanceUrl(customerId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.PostAsJsonAsync(PromisesUrl, NewPromise(scenario.FirstStoreId, customerId, 10))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.DeleteAsync($"{PromisesUrl}/{promiseId}?rowVersion=1")).StatusCode);

        var viewer = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Customers.Promise.View);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(PromisesUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(PromisesUrl, NewPromise(scenario.FirstStoreId, customerId, 10))).StatusCode);
    }

    [Fact]
    public async Task One_company_never_sees_or_changes_the_promises_of_another()
    {
        var companyA = await OwnerCompanyScenario.CreateAsync(environment);
        var companyB = await OwnerCompanyScenario.CreateAsync(environment);
        var customerOfA = await CreateCustomerAsync(companyA.OwnerClient, "Customer of A");
        var promiseOfA = await CreatePromiseAsync(companyA.OwnerClient, NewPromise(companyA.FirstStoreId, customerOfA, 100));

        Assert.Equal(0, (await ListAsync(companyB.OwnerClient)).GetProperty("promises").GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.GetAsync($"{PromisesUrl}/{promiseOfA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.DeleteAsync($"{PromisesUrl}/{promiseOfA}?rowVersion=1")).StatusCode);
        // company A's store and customer do not exist for company B, so the request is refused as invalid
        Assert.Equal(HttpStatusCode.BadRequest, (await companyB.OwnerClient.PostAsJsonAsync(PromisesUrl, NewPromise(companyA.FirstStoreId, customerOfA, 5))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.GetAsync(BalanceUrl(customerOfA))).StatusCode);
    }
}
