using System.Net;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using EFootwear.Tests.Parties;
using static EFootwear.Tests.Accounting.VoucherTestHelpers;

namespace EFootwear.Tests.Accounting;

/// <summary>
/// Account Ledger, Account Balance, Trial Balance, Detailed Trial Balance and General Journal (docx 8.1 to 8.4, 8.28). The figures come from journal lines only;
/// the trial balance must always balance, the totals must not be affected by paging, and the reports follow permissions and store scope.
/// </summary>
[Collection(ApiIntegrationCollection.Name)]
public class AccountReportApiTests(ApiTestEnvironment environment)
{
    private static readonly string ReportsUrl = TestScenarioBuilder.Url(ApiRoutes.AccountReports.Root);

    private static async Task<JsonElement> GetAsync(HttpClient client, string relativeUrl) =>
        await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync($"{ReportsUrl}/{relativeUrl}"));

    /// <summary>
    /// A company with: a vendor paid 10,000 on 2026-10-15 (before the ledger range) and 60,000 on 2026-11-10 from the store cash account,
    /// and a customer who paid 25,000 into the same cash account on 2026-11-10.
    /// </summary>
    private async Task<(OwnerCompanyScenario Scenario, Guid VendorId, Guid CustomerId, Guid CashAccountId)> CreateBusyCompanyAsync()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var vendorId = await PartyTestHelpers.CreatePartyAsync(scenario.OwnerClient, "Report Vendor", isCustomer: false, isVendor: true);
        var customerId = await PartyTestHelpers.CreatePartyAsync(scenario.OwnerClient, "Report Customer");
        var cashAccountId = await FirstStoreCashAccountIdAsync(environment, scenario.CompanyId);
        await PostVoucherAsync(scenario.OwnerClient, PaymentUrl, VoucherBody(scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 10_000, voucherDate: "2026-10-15", description: "October payment"));
        await PostVoucherAsync(scenario.OwnerClient, PaymentUrl, VoucherBody(scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 60_000, description: "November payment"));
        await PostVoucherAsync(scenario.OwnerClient, ReceiptUrl, VoucherBody(scenario.FirstStoreId, AccountTarget(cashAccountId), PartyTarget(customerId), 25_000, description: "Customer receipt"));
        return (scenario, vendorId, customerId, cashAccountId);
    }

    [Fact]
    public async Task The_ledger_shows_the_opening_balance_running_balance_and_totals_of_the_whole_range()
    {
        var (scenario, _, _, cashAccountId) = await CreateBusyCompanyAsync();

        var ledger = await GetAsync(scenario.OwnerClient,
            $"{ApiRoutes.AccountReports.Ledger}?accountId={cashAccountId}&fromDate=2026-11-01&toDate=2026-11-30&includeDescriptions=true");

        Assert.Equal("CASH-S1", ledger.GetProperty("account").GetProperty("code").GetString());
        Assert.Equal(10_000, ledger.GetProperty("openingBalanceAmount").GetInt64());
        Assert.Equal("CREDIT", ledger.GetProperty("openingBalanceSideCode").GetString());
        Assert.Equal(25_000, ledger.GetProperty("totalDebitAmount").GetInt64());
        Assert.Equal(60_000, ledger.GetProperty("totalCreditAmount").GetInt64());
        Assert.Equal(45_000, ledger.GetProperty("closingBalanceAmount").GetInt64());
        Assert.Equal("CREDIT", ledger.GetProperty("closingBalanceSideCode").GetString());

        var rows = ledger.GetProperty("lines").GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(70_000, rows[0].GetProperty("runningBalanceAmount").GetInt64());   // 10,000 opening + 60,000 paid out
        Assert.Equal(45_000, rows[1].GetProperty("runningBalanceAmount").GetInt64());
        Assert.Equal("November payment", rows[0].GetProperty("description").GetString());

        // the same by account number, without narrations, and one row per page: totals stay those of the whole range
        var byCode = await GetAsync(scenario.OwnerClient,
            $"{ApiRoutes.AccountReports.Ledger}?accountCode=CASH-S1&fromDate=2026-11-01&toDate=2026-11-30&includeDescriptions=false&pageSize=1&pageNumber=2&storeId={scenario.FirstStoreId}");
        var pageRows = byCode.GetProperty("lines").GetProperty("items").EnumerateArray().ToList();
        Assert.Single(pageRows);
        Assert.Equal(JsonValueKind.Null, pageRows[0].GetProperty("description").ValueKind);
        Assert.Equal(2, byCode.GetProperty("lines").GetProperty("totalCount").GetInt64());
        Assert.Equal(60_000, byCode.GetProperty("totalCreditAmount").GetInt64());
        Assert.Equal(45_000, byCode.GetProperty("closingBalanceAmount").GetInt64());
    }

    [Fact]
    public async Task The_ledger_validates_its_filters_and_cannot_see_another_companys_account()
    {
        var (scenario, _, _, cashAccountId) = await CreateBusyCompanyAsync();
        var otherCompany = await OwnerCompanyScenario.CreateAsync(environment);

        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.GetAsync($"{ReportsUrl}/{ApiRoutes.AccountReports.Ledger}?fromDate=2026-11-01&toDate=2026-11-30")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.GetAsync($"{ReportsUrl}/{ApiRoutes.AccountReports.Ledger}?accountId={cashAccountId}&fromDate=2026-12-01&toDate=2026-11-01")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.OwnerClient.GetAsync($"{ReportsUrl}/{ApiRoutes.AccountReports.Ledger}?accountCode=NOPE&fromDate=2026-11-01&toDate=2026-11-30")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherCompany.OwnerClient.GetAsync(
            $"{ReportsUrl}/{ApiRoutes.AccountReports.Ledger}?accountId={cashAccountId}&fromDate=2026-11-01&toDate=2026-11-30")).StatusCode);
    }

    [Fact]
    public async Task The_trial_balance_balances_and_the_detailed_one_lists_every_account_under_its_head_without_double_counting()
    {
        var (scenario, _, _, _) = await CreateBusyCompanyAsync();
        var journalDebit = await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select coalesce(sum(debit), 0)::bigint from acct.journal_lines where company_id = $1", scenario.CompanyId);

        var summary = await GetAsync(scenario.OwnerClient, $"{ApiRoutes.AccountReports.TrialBalance}?asOfDate=2026-11-30");
        Assert.True(summary.GetProperty("isBalanced").GetBoolean());
        Assert.Equal(journalDebit, summary.GetProperty("totalDebitAmount").GetInt64());
        Assert.Equal(summary.GetProperty("totalDebitAmount").GetInt64(), summary.GetProperty("totalCreditAmount").GetInt64());
        Assert.Equal(summary.GetProperty("totalBalanceDebitAmount").GetInt64(), summary.GetProperty("totalBalanceCreditAmount").GetInt64());
        Assert.All(summary.GetProperty("rows").EnumerateArray(), row => Assert.Equal("GROUP", row.GetProperty("rowKindCode").GetString()));

        var detailed = await GetAsync(scenario.OwnerClient, $"{ApiRoutes.AccountReports.DetailTrialBalance}?asOfDate=2026-11-30");
        var detailedRows = detailed.GetProperty("rows").EnumerateArray().ToList();
        Assert.Contains(detailedRows, row => row.GetProperty("rowKindCode").GetString() == "ACCOUNT" && row.GetProperty("accountCode").GetString() == "CASH-S1");
        // the totals add the group rows only, so the detailed report totals the same as the summary
        Assert.Equal(summary.GetProperty("totalDebitAmount").GetInt64(), detailed.GetProperty("totalDebitAmount").GetInt64());
        Assert.True(detailed.GetProperty("isBalanced").GetBoolean());

        // as on an earlier date only the October payment counts
        var october = await GetAsync(scenario.OwnerClient, $"{ApiRoutes.AccountReports.TrialBalance}?asOfDate=2026-10-31");
        Assert.Equal(10_000, october.GetProperty("totalDebitAmount").GetInt64());
        Assert.True(october.GetProperty("isBalanced").GetBoolean());
    }

    [Fact]
    public async Task The_account_balance_report_filters_by_type_and_by_amount_type_and_lists_party_balances()
    {
        var (scenario, vendorId, _, _) = await CreateBusyCompanyAsync();

        var assets = await GetAsync(scenario.OwnerClient, $"{ApiRoutes.AccountReports.Balances}?asOfDate=2026-11-30&accountTypeFilter=MAIN_TYPE:ASSET");
        var cashRow = assets.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("code").GetString() == "CASH-S1");
        Assert.Equal(45_000, cashRow.GetProperty("creditAmount").GetInt64());
        Assert.Equal(0, cashRow.GetProperty("debitAmount").GetInt64());

        var debitOnly = await GetAsync(scenario.OwnerClient, $"{ApiRoutes.AccountReports.Balances}?asOfDate=2026-11-30&accountTypeFilter=MAIN_TYPE:ASSET&amountTypeFilter=DEBIT");
        Assert.DoesNotContain(debitOnly.GetProperty("rows").EnumerateArray(), row => row.GetProperty("code").GetString() == "CASH-S1");

        var vendors = await GetAsync(scenario.OwnerClient, $"{ApiRoutes.AccountReports.Balances}?asOfDate=2026-11-30&accountTypeFilter=PARTIES:VENDOR");
        var vendorRow = vendors.GetProperty("rows").EnumerateArray().Single();
        Assert.Equal(vendorId, vendorRow.GetProperty("partyId").GetGuid());
        Assert.Equal(70_000, vendorRow.GetProperty("debitAmount").GetInt64());
        Assert.Equal(70_000, vendors.GetProperty("totalDebitAmount").GetInt64());

        var vendorsInOctober = await GetAsync(scenario.OwnerClient, $"{ApiRoutes.AccountReports.Balances}?asOfDate=2026-10-31&accountTypeFilter=PARTIES:VENDOR");
        Assert.Equal(10_000, vendorsInOctober.GetProperty("totalDebitAmount").GetInt64());

        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.GetAsync($"{ReportsUrl}/{ApiRoutes.AccountReports.Balances}?asOfDate=2026-11-30&accountTypeFilter=NOPE")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.GetAsync($"{ReportsUrl}/{ApiRoutes.AccountReports.Balances}?asOfDate=2026-11-30&accountTypeFilter=MAIN_TYPE:ASSET&amountTypeFilter=SIDEWAYS")).StatusCode);

        var types = await GetAsync(scenario.OwnerClient, ApiRoutes.AccountReports.BalanceTypes);
        var filterCodes = types.EnumerateArray().Select(option => option.GetProperty("filterCode").GetString()!).ToList();
        Assert.Contains("MAIN_TYPE:ASSET", filterCodes);
        Assert.Contains("GROUP:103", filterCodes);
        Assert.Contains("PARTIES:CUSTOMER", filterCodes);
    }

    [Fact]
    public async Task The_general_journal_lists_voucher_lines_with_their_type_and_number_and_totals_equal()
    {
        var (scenario, _, _, _) = await CreateBusyCompanyAsync();

        var all = await GetAsync(scenario.OwnerClient, $"{ApiRoutes.AccountReports.GeneralJournal}?fromDate=2026-11-01&toDate=2026-11-30");
        Assert.Equal(4, all.GetProperty("lines").GetProperty("totalCount").GetInt64());   // two vouchers in November, two lines each
        Assert.Equal(all.GetProperty("totalDebitAmount").GetInt64(), all.GetProperty("totalCreditAmount").GetInt64());
        Assert.Equal(85_000, all.GetProperty("totalDebitAmount").GetInt64());

        var payments = await GetAsync(scenario.OwnerClient, $"{ApiRoutes.AccountReports.GeneralJournal}?fromDate=2026-11-01&toDate=2026-11-30&typeCode=payment");
        var paymentLines = payments.GetProperty("lines").GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, paymentLines.Count);
        Assert.All(paymentLines, line =>
        {
            Assert.Equal("PAYMENT", line.GetProperty("typeCode").GetString());
            Assert.Equal(2, line.GetProperty("voucherNumber").GetInt64());   // the November payment is the second payment voucher
            Assert.Equal(scenario.FirstStoreId, line.GetProperty("storeId").GetGuid());
        });
        Assert.Equal(60_000, payments.GetProperty("totalDebitAmount").GetInt64());

        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.GetAsync($"{ReportsUrl}/{ApiRoutes.AccountReports.GeneralJournal}?fromDate=2026-11-01&toDate=2026-11-30&typeCode=bad-type!")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.GetAsync($"{ReportsUrl}/{ApiRoutes.AccountReports.GeneralJournal}?fromDate=2026-11-30&toDate=2026-11-01")).StatusCode);
    }

    [Fact]
    public async Task A_voided_voucher_shows_in_the_general_journal_as_its_reversal_with_the_voucher_number()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var vendorId = await PartyTestHelpers.CreatePartyAsync(scenario.OwnerClient, "Vendor Void Journal", isCustomer: false, isVendor: true);
        var cashAccountId = await FirstStoreCashAccountIdAsync(environment, scenario.CompanyId);
        var voucher = await PostVoucherAsync(scenario.OwnerClient, PaymentUrl, VoucherBody(scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 3_000));
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, $"{VouchersUrl}/{voucher.GetProperty("id").GetGuid()}/void", new { reason = "Mistake" });

        // the reversal is dated by the database's current date, which can be a day ahead of UTC late in the evening
        var today = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2).ToString("yyyy-MM-dd");
        var journal = await GetAsync(scenario.OwnerClient, $"{ApiRoutes.AccountReports.GeneralJournal}?fromDate=2026-01-01&toDate={today}&typeCode=VOUCHER_VOID");
        var lines = journal.GetProperty("lines").GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, line => Assert.Equal(1, line.GetProperty("voucherNumber").GetInt64()));
        Assert.Equal(3_000, journal.GetProperty("totalDebitAmount").GetInt64());
    }

    [Fact]
    public async Task Every_report_needs_its_screen_permission_and_the_legacy_report_code()
    {
        var (scenario, _, _, _) = await CreateBusyCompanyAsync();
        var trialBalanceUrl = $"{ReportsUrl}/{ApiRoutes.AccountReports.TrialBalance}?asOfDate=2026-11-30";

        var nothing = await scenario.SignInManagerWithPermissionsAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.GetAsync(trialBalanceUrl)).StatusCode);

        var screenOnly = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Reports.TrialBalance.View);
        Assert.Equal(HttpStatusCode.Forbidden, (await screenOnly.GetAsync(trialBalanceUrl)).StatusCode);

        var legacyOnly = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.RestrictedOperations.Reporting.ReportAccounts);
        Assert.Equal(HttpStatusCode.Forbidden, (await legacyOnly.GetAsync(trialBalanceUrl)).StatusCode);

        var both = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Reports.TrialBalance.View, PermissionCodes.RestrictedOperations.Reporting.ReportAccounts);
        Assert.Equal(HttpStatusCode.OK, (await both.GetAsync(trialBalanceUrl)).StatusCode);
        // the trial balance permission does not open the general journal
        Assert.Equal(HttpStatusCode.Forbidden, (await both.GetAsync($"{ReportsUrl}/{ApiRoutes.AccountReports.GeneralJournal}?fromDate=2026-11-01&toDate=2026-11-30")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.PlatformClient.GetAsync(trialBalanceUrl)).StatusCode);
    }

    [Fact]
    public async Task A_store_manager_sees_only_their_stores_figures_and_cannot_ask_for_another_store()
    {
        var (scenario, _, _, _) = await CreateBusyCompanyAsync();
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        var secondStoreManager = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [secondStoreId],
            PermissionCodes.Reports.TrialBalance.View, PermissionCodes.RestrictedOperations.Reporting.ReportAccounts);

        Assert.Equal(HttpStatusCode.Forbidden, (await secondStoreManager.GetAsync(
            $"{ReportsUrl}/{ApiRoutes.AccountReports.TrialBalance}?asOfDate=2026-11-30&storeId={scenario.FirstStoreId}")).StatusCode);

        // without a store filter, row-level security leaves only the store's own lines: nothing was posted there
        var ownView = await OwnerCompanyScenario.ReadJsonAsync(await secondStoreManager.GetAsync($"{ReportsUrl}/{ApiRoutes.AccountReports.TrialBalance}?asOfDate=2026-11-30"));
        Assert.Equal(0, ownView.GetProperty("totalDebitAmount").GetInt64());
        Assert.True(ownView.GetProperty("isBalanced").GetBoolean());
    }
}
