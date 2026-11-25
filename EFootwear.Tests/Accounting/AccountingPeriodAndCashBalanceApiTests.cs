using System.Net;
using System.Net.Http.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using EFootwear.Tests.Contacts;
using static EFootwear.Tests.Accounting.VoucherTestHelpers;

namespace EFootwear.Tests.Accounting;

/// <summary>Closing and reopening months of the books (Part 15) and the cash and bank balances of cash management.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class AccountingPeriodAndCashBalanceApiTests(ApiTestEnvironment environment)
{
    private static readonly string PeriodsUrl = TestScenarioBuilder.Url(ApiRoutes.AccountingPeriods.Root);
    private static readonly string CashAndBankUrl = TestScenarioBuilder.Url(ApiRoutes.CashAndBankBalances.Root);

    [Fact]
    public async Task Periods_are_listed_newest_first_and_can_be_closed_and_reopened_with_who_and_when()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var vendorId = await ContactTestHelpers.CreateVendorAsync(scenario.OwnerClient, "Vendor For Periods");
        var cashAccountId = await FirstStoreCashAccountIdAsync(environment, scenario.CompanyId);
        await PostVoucherAsync(scenario.OwnerClient, PaymentUrl, VoucherBody(scenario.FirstStoreId, VendorTarget(vendorId), AccountTarget(cashAccountId), 1_000, voucherDate: "2026-09-12"));

        var periods = (await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(PeriodsUrl))).EnumerateArray().ToList();
        Assert.True(periods.Count >= 2);
        Assert.True(string.CompareOrdinal(periods[0].GetProperty("periodStart").GetString(), periods[1].GetProperty("periodStart").GetString()) > 0);   // newest first
        Assert.Equal("2026-09-01", periods[^1].GetProperty("periodStart").GetString());                                                                 // from the first entry
        Assert.All(periods, period => Assert.Equal("OPEN", period.GetProperty("statusCode").GetString()));

        // any date inside the month closes that month
        var closed = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, $"{PeriodsUrl}/{ApiRoutes.AccountingPeriods.Close}", new { periodStart = "2026-09-20" });
        Assert.Equal("2026-09-01", closed.GetProperty("periodStart").GetString());
        Assert.Equal("CLOSED", closed.GetProperty("statusCode").GetString());
        Assert.False(string.IsNullOrEmpty(closed.GetProperty("closedByName").GetString()));

        var afterClose = (await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(PeriodsUrl))).EnumerateArray().ToList();
        Assert.Equal("CLOSED", afterClose.Single(period => period.GetProperty("periodStart").GetString() == "2026-09-01").GetProperty("statusCode").GetString());

        var reopened = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, $"{PeriodsUrl}/{ApiRoutes.AccountingPeriods.Reopen}", new { periodStart = "2026-09-01" });
        Assert.Equal("OPEN", reopened.GetProperty("statusCode").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, reopened.GetProperty("closedAt").ValueKind);
    }

    [Fact]
    public async Task A_month_that_has_not_started_cannot_be_closed()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var farFuture = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(2).ToString("yyyy-MM-dd");
        var response = await scenario.OwnerClient.PostAsJsonAsync($"{PeriodsUrl}/{ApiRoutes.AccountingPeriods.Close}", new { periodStart = farFuture });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_voucher_of_a_closed_month_can_still_be_voided_because_the_reversal_is_dated_today()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var vendorId = await ContactTestHelpers.CreateVendorAsync(scenario.OwnerClient, "Vendor Closed Month Void");
        var cashAccountId = await FirstStoreCashAccountIdAsync(environment, scenario.CompanyId);
        var voucher = await PostVoucherAsync(scenario.OwnerClient, PaymentUrl, VoucherBody(scenario.FirstStoreId, VendorTarget(vendorId), AccountTarget(cashAccountId), 2_000, voucherDate: "2026-04-10"));
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, $"{PeriodsUrl}/{ApiRoutes.AccountingPeriods.Close}", new { periodStart = "2026-04-01" });

        var voided = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, $"{VouchersUrl}/{voucher.GetProperty("id").GetGuid()}/void", new { reason = "Corrected after close" });

        Assert.Equal("VOID", voided.GetProperty("statusCode").GetString());
        Assert.Equal(0, await VendorNetBalanceAsync(environment, scenario.CompanyId, vendorId));
        // the closed month itself still holds only the original entry
        Assert.Equal(1L, await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from acct.journal_entries where company_id = $1 and entry_date = date '2026-04-10'", scenario.CompanyId));
    }

    [Fact]
    public async Task Closing_needs_the_approve_permission_and_listing_the_view_permission()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var closeUrl = $"{PeriodsUrl}/{ApiRoutes.AccountingPeriods.Close}";

        var nothing = await scenario.SignInManagerWithPermissionsAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.GetAsync(PeriodsUrl)).StatusCode);

        var viewer = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Voucher.AccountingPeriods.View);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(PeriodsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(closeUrl, new { periodStart = "2026-02-01" })).StatusCode);

        var approver = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Voucher.AccountingPeriods.Approve);
        Assert.Equal(HttpStatusCode.OK, (await approver.PostAsJsonAsync(closeUrl, new { periodStart = "2026-02-01" })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.PlatformClient.GetAsync(PeriodsUrl)).StatusCode);
    }

    [Fact]
    public async Task Another_company_has_its_own_periods()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var otherCompany = await OwnerCompanyScenario.CreateAsync(environment);
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, $"{PeriodsUrl}/{ApiRoutes.AccountingPeriods.Close}", new { periodStart = "2026-05-01" });

        var otherPeriods = (await OwnerCompanyScenario.ReadJsonAsync(await otherCompany.OwnerClient.GetAsync(PeriodsUrl))).EnumerateArray().ToList();
        Assert.DoesNotContain(otherPeriods, period => period.GetProperty("statusCode").GetString() == "CLOSED");
    }

    [Fact]
    public async Task Cash_and_bank_balances_are_derived_from_the_journal_per_account_and_store()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        var customerId = await ContactTestHelpers.CreateCustomerAsync(scenario.OwnerClient, "Cash And Bank Customer");
        var cashAccountId = await FirstStoreCashAccountIdAsync(environment, scenario.CompanyId);
        var bankAccountId = await AccountIdAsync(environment, scenario.CompanyId, "1020");

        await PostVoucherAsync(scenario.OwnerClient, ReceiptUrl, VoucherBody(scenario.FirstStoreId, AccountTarget(cashAccountId), CustomerTarget(customerId), 25_000));
        await PostVoucherAsync(scenario.OwnerClient, ReceiptUrl, VoucherBody(scenario.FirstStoreId, AccountTarget(bankAccountId), CustomerTarget(customerId), 5_000, paymentMode: "BANK"));
        await PostVoucherAsync(scenario.OwnerClient, ExpenseUrl, VoucherBody(scenario.FirstStoreId, AccountTarget(await AccountIdAsync(environment, scenario.CompanyId, "EXP-RENT")), AccountTarget(cashAccountId), 4_000));

        var balances = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{CashAndBankUrl}?asOfDate=2026-11-30"));
        var accounts = balances.GetProperty("accounts").EnumerateArray().ToList();
        var cash = accounts.Single(account => account.GetProperty("accountCode").GetString() == "CASH-S1");
        Assert.Equal("CASH", cash.GetProperty("kindCode").GetString());
        Assert.Equal(21_000, cash.GetProperty("balanceAmount").GetInt64());
        Assert.Equal("DEBIT", cash.GetProperty("balanceSideCode").GetString());
        var bank = accounts.Single(account => account.GetProperty("accountCode").GetString() == "1020");
        Assert.Equal("BANK", bank.GetProperty("kindCode").GetString());
        Assert.Equal(5_000, bank.GetProperty("balanceAmount").GetInt64());
        Assert.Equal(scenario.FirstStoreId, bank.GetProperty("storeId").GetGuid());   // the line's store, although the bank account belongs to the head office
        Assert.Equal(21_000, balances.GetProperty("totalCashAmount").GetInt64());
        Assert.Equal(5_000, balances.GetProperty("totalBankAmount").GetInt64());

        // as on an earlier date there is nothing; for the empty second store there are no rows
        var earlier = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{CashAndBankUrl}?asOfDate=2026-01-01"));
        Assert.Equal(0, earlier.GetProperty("accounts").GetArrayLength());
        var secondStore = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{CashAndBankUrl}?asOfDate=2026-11-30&storeId={secondStoreId}"));
        Assert.Equal(0, secondStore.GetProperty("accounts").GetArrayLength());
    }

    [Fact]
    public async Task Cash_and_bank_balances_follow_permissions_and_store_scope()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        var url = $"{CashAndBankUrl}?asOfDate=2026-11-30";

        var nothing = await scenario.SignInManagerWithPermissionsAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.GetAsync(url)).StatusCode);

        var secondStoreManager = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [secondStoreId], PermissionCodes.Voucher.CashAndBankBalances.View);
        Assert.Equal(HttpStatusCode.OK, (await secondStoreManager.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await secondStoreManager.GetAsync($"{url}&storeId={scenario.FirstStoreId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.PlatformClient.GetAsync(url)).StatusCode);
    }
}
