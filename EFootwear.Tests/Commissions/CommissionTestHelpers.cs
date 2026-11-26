using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Tests.Catalogue;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using EFootwear.Tests.Payroll;
using EFootwear.Tests.Sales;

namespace EFootwear.Tests.Commissions;

/// <summary>Builders shared by the commission tests: urls, plan bodies, a sale made by a named salesman, and the database reads that check the entries.</summary>
public static class CommissionTestHelpers
{
    public const string PlanFirstDay = "2026-01-01";

    public static readonly string CommissionsUrl = TestScenarioBuilder.Url(ApiRoutes.Commissions.Root);
    public static string PlansUrl => $"{CommissionsUrl}/{ApiRoutes.Commissions.Plans}";
    public static string PlanUrl(Guid planId) => $"{PlansUrl}/{planId}";
    public static string PlanRulesUrl(Guid planId) => $"{PlanUrl(planId)}/rules";
    public static string EndRuleUrl(Guid planId, Guid ruleId) => $"{PlanRulesUrl(planId)}/{ruleId}/end";
    public static string EntriesUrl => $"{CommissionsUrl}/{ApiRoutes.Commissions.Entries}";
    public static string EntryTotalsUrl => $"{CommissionsUrl}/{ApiRoutes.Commissions.EntryTotals}";
    public static string ApproveUrl => $"{CommissionsUrl}/{ApiRoutes.Commissions.ApproveEntries}";

    public static string SummaryReportUrl(Guid? storeId, string from = "2026-11-01", string to = "2026-11-30", Guid? staffId = null) =>
        $"{CommissionsUrl}/{ApiRoutes.Commissions.CommissionReportSummary}?fromDate={from}&toDate={to}" + Filters(storeId, staffId);

    public static string DetailReportUrl(Guid? storeId, string from = "2026-11-01", string to = "2026-11-30", Guid? staffId = null) =>
        $"{CommissionsUrl}/{ApiRoutes.Commissions.CommissionReportDetail}?fromDate={from}&toDate={to}" + Filters(storeId, staffId);

    public static string AccrualReportUrl(Guid? storeId, string from = "2026-11-01", string to = "2026-11-30") =>
        $"{CommissionsUrl}/{ApiRoutes.Commissions.AccrualAndPaymentReport}?fromDate={from}&toDate={to}" + Filters(storeId, null);

    private static string Filters(Guid? storeId, Guid? staffId) =>
        (storeId is null ? "" : $"&storeId={storeId}") + (staffId is null ? "" : $"&staffId={staffId}");

    /// <summary>A plan body. By default: salesmen, net sale, a percentage, every store, in force from the start of the year.</summary>
    public static object PlanBody(
        string name, IReadOnlyList<Guid>? memberStaffIds = null, decimal? rate = 3m, string appliesTo = "SALESMAN", string basis = "NET_SALE",
        string calculationType = "PERCENT", long? fixedAmount = null, long minimumStoreSales = 0, Guid? storeId = null, string validFrom = PlanFirstDay,
        string? validTo = null, bool isActive = true, long? rowVersion = null) =>
        new
        {
            name,
            appliesToCode = appliesTo,
            basisCode = basis,
            calculationTypeCode = calculationType,
            defaultRate = calculationType == "PERCENT" ? rate : null,
            defaultFixedAmountPerPiece = fixedAmount,
            minimumStoreSales,
            storeId,
            validFrom,
            validTo,
            isActive,
            memberStaffIds = memberStaffIds ?? [],
            rowVersion,
        };

    public static Task<JsonElement> CreatePlanAsync(HttpClient client, object body) => OwnerCompanyScenario.PostJsonAsync(client, PlansUrl, body);

    /// <summary>A plan for one salesman at a percentage: "this salesman gets 3 percent".</summary>
    public static Task<JsonElement> CreateSalesmanPlanAsync(HttpClient client, Guid staffId, decimal rate = 3m, string name = "Salesman plan") =>
        CreatePlanAsync(client, PlanBody(name, [staffId], rate));

    /// <summary>A cash sale of one line in which the given salesman is credited with the sale.</summary>
    public static async Task<JsonElement> PostSaleBySalesmanAsync(
        ApiTestEnvironment environment, OwnerCompanyScenario scenario, SalesTestHelpers.StockedItem item, decimal quantity, Guid? salesmanStaffId, Guid? clientTransactionId = null)
    {
        var cashAccountId = await SalesTestHelpers.CashAccountIdAsync(environment, scenario.CompanyId);
        var body = new
        {
            storeId = scenario.FirstStoreId,
            saleDate = SalesTestHelpers.TestSaleDate,
            clientTransactionId = clientTransactionId ?? Guid.NewGuid(),
            customerId = (Guid?)null,
            salesmanStaffId,
            walkInCustomerName = (string?)null,
            walkInCustomerMobile = (string?)null,
            posTerminalDeviceId = (Guid?)null,
            description = (string?)null,
            lines = new[] { SalesTestHelpers.Line(item.VariantId, quantity, item.ShelfPrice) },
            payments = new[] { SalesTestHelpers.CashPayment(cashAccountId, item.ShelfPrice * (long)quantity) },
            cashReturnedAmount = 0L,
        };
        return await SalesTestHelpers.PostSaleAsync(scenario.OwnerClient, body);
    }

    public static async Task<JsonElement> ListEntriesAsync(HttpClient client, string query = "") =>
        await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(EntriesUrl + (query == "" ? "" : "?" + query)));

    public static async Task<JsonElement> GetTotalsAsync(HttpClient client, string query = "") =>
        await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(EntryTotalsUrl + (query == "" ? "" : "?" + query)));

    public static async Task<JsonElement> ApproveAsync(HttpClient client, Guid storeId, string from = "2026-11-01", string to = "2026-11-30")
    {
        var response = await client.PostAsJsonAsync(ApproveUrl, new { storeId, fromDate = from, toDate = to });
        await TestScenarioBuilder.EnsureSuccessAsync(response);
        return await OwnerCompanyScenario.ReadJsonAsync(response);
    }

    public static async Task<long> EntryCountInDatabaseAsync(ApiTestEnvironment environment, Guid companyId) =>
        await environment.Database.QueryScalarAsOwnerAsync<long>("select count(*) from hr.commission_entries where company_id = $1", companyId);

    public static async Task<(OwnerCompanyScenario Scenario, SalesTestHelpers.StockedItem Item, Guid SalesmanId)> NewShopWithSalesmanAsync(
        ApiTestEnvironment environment, long shelfPrice = 1_000, decimal stock = 20)
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var lookups = await CatalogueTestHelpers.CreateLookupsAsync(scenario.OwnerClient);
        var item = await SalesTestHelpers.CreateStockedItemAsync(environment, scenario, lookups, "Gents Sandal", shelfPrice, stock);
        var salesmanId = await PayrollTestHelpers.CreateStaffAsync(environment, scenario, "Zaman Salesman", scenario.FirstStoreId, 0);
        return (scenario, item, salesmanId);
    }
}
