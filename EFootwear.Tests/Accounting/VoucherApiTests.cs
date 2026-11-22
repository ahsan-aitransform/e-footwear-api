using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using EFootwear.Tests.Parties;
using static EFootwear.Tests.Accounting.VoucherTestHelpers;

namespace EFootwear.Tests.Accounting;

/// <summary>
/// Payment, Receipt, Expense and Journal vouchers, View Voucher and the Voucher Report (docx 6.1 to 6.6). Every voucher is one balanced journal entry
/// of two lines posted by acct.post_voucher; voiding reverses it and keeps the voucher; retries are safe; stores and companies are isolated.
/// </summary>
[Collection(ApiIntegrationCollection.Name)]
public class VoucherApiTests(ApiTestEnvironment environment)
{
    [Fact]
    public async Task A_payment_voucher_posts_one_balanced_entry_and_the_vendor_balance_equals_the_ledger()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var vendorId = await PartyTestHelpers.CreatePartyAsync(scenario.OwnerClient, "Zubair Umair Shoes", isCustomer: false, isVendor: true);
        var cashAccountId = await FirstStoreCashAccountIdAsync(environment, scenario.CompanyId);

        var voucher = await PostVoucherAsync(scenario.OwnerClient, PaymentUrl,
            VoucherBody(scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 60_000, referenceNumber: "CHQ-77"));

        Assert.Equal("PAYMENT", voucher.GetProperty("voucherTypeCode").GetString());
        Assert.Equal(1, voucher.GetProperty("voucherNumber").GetInt64());
        Assert.Equal("POSTED", voucher.GetProperty("statusCode").GetString());
        Assert.Equal(60_000, voucher.GetProperty("amount").GetInt64());
        Assert.Equal("CHQ-77", voucher.GetProperty("referenceNumber").GetString());

        var lines = voucher.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(60_000, lines.Sum(line => line.GetProperty("debitAmount").GetInt64()));
        Assert.Equal(60_000, lines.Sum(line => line.GetProperty("creditAmount").GetInt64()));
        Assert.Contains(lines, line => line.GetProperty("partyId").ValueKind == JsonValueKind.String && line.GetProperty("debitAmount").GetInt64() == 60_000);

        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));
        Assert.Equal(60_000, await PartyNetBalanceAsync(environment, scenario.CompanyId, vendorId));
        Assert.Equal(60_000, await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select debit_total - credit_total from party.party_balances where company_id = $1 and party_id = $2", scenario.CompanyId, vendorId));
        Assert.Equal(-60_000, await NetBalanceOfAccountAsync(environment, scenario.CompanyId, cashAccountId));
    }

    [Fact]
    public async Task Voucher_numbers_count_up_per_type_and_store_and_the_entry_context_previews_the_next_one()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var customerId = await PartyTestHelpers.CreatePartyAsync(scenario.OwnerClient, "Walk-in Customer Two");
        var cashAccountId = await FirstStoreCashAccountIdAsync(environment, scenario.CompanyId);

        var contextBefore = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(
            $"{VouchersUrl}/{ApiRoutes.Vouchers.ReceiptEntryContext}?storeId={scenario.FirstStoreId}"));
        Assert.Equal(1, contextBefore.GetProperty("nextVoucherNumber").GetInt64());

        var first = await PostVoucherAsync(scenario.OwnerClient, ReceiptUrl,
            VoucherBody(scenario.FirstStoreId, AccountTarget(cashAccountId), PartyTarget(customerId), 5_000));
        var second = await PostVoucherAsync(scenario.OwnerClient, ReceiptUrl,
            VoucherBody(scenario.FirstStoreId, AccountTarget(cashAccountId), PartyTarget(customerId), 7_000));
        var journalVoucher = await PostVoucherAsync(scenario.OwnerClient, JournalUrl,
            VoucherBody(scenario.FirstStoreId, AccountTarget(cashAccountId), PartyTarget(customerId), 1_000));

        Assert.Equal(1, first.GetProperty("voucherNumber").GetInt64());
        Assert.Equal(2, second.GetProperty("voucherNumber").GetInt64());
        Assert.Equal(1, journalVoucher.GetProperty("voucherNumber").GetInt64());   // a separate series per voucher type

        var contextAfter = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(
            $"{VouchersUrl}/{ApiRoutes.Vouchers.ReceiptEntryContext}?storeId={scenario.FirstStoreId}"));
        Assert.Equal(3, contextAfter.GetProperty("nextVoucherNumber").GetInt64());
    }

    [Fact]
    public async Task An_expense_voucher_debits_an_expense_account_and_credits_cash()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var rentAccountId = await AccountIdAsync(environment, scenario.CompanyId, "EXP-RENT");
        var cashAccountId = await FirstStoreCashAccountIdAsync(environment, scenario.CompanyId);

        var voucher = await PostVoucherAsync(scenario.OwnerClient, ExpenseUrl,
            VoucherBody(scenario.FirstStoreId, AccountTarget(rentAccountId), AccountTarget(cashAccountId), 45_000, description: "Shop rent"));

        Assert.Equal("EXPENSE", voucher.GetProperty("voucherTypeCode").GetString());
        Assert.Equal(45_000, await NetBalanceOfAccountAsync(environment, scenario.CompanyId, rentAccountId));
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));
    }

    [Fact]
    public async Task The_database_refuses_an_unbalanced_journal_entry()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var cashAccountId = await FirstStoreCashAccountIdAsync(environment, scenario.CompanyId);

        // One line only: the deferred trigger acct.check_entry_balanced fails the whole statement at commit.
        await Assert.ThrowsAnyAsync<Exception>(() => environment.Database.ExecuteAsOwnerAsync("""
            with entry as (
                insert into acct.journal_entries (company_id, id, entry_date, store_id, source_type, description)
                values ($1, uuidv7(), date '2026-11-10', $2, 'TEST', 'unbalanced') returning id
            )
            insert into acct.journal_lines (company_id, entry_id, entry_date, line_no, store_id, account_id, debit, credit)
            select $1, id, date '2026-11-10', 1, $2, $3, 500, 0 from entry
            """, scenario.CompanyId, scenario.FirstStoreId, cashAccountId));
    }

    [Fact]
    public async Task Bad_vouchers_are_refused_with_a_clear_error()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var vendorId = await PartyTestHelpers.CreatePartyAsync(scenario.OwnerClient, "Vendor For Bad Vouchers", isCustomer: false, isVendor: true);
        var otherVendorId = await PartyTestHelpers.CreatePartyAsync(scenario.OwnerClient, "Second Vendor", isCustomer: false, isVendor: true);
        var cashAccountId = await FirstStoreCashAccountIdAsync(environment, scenario.CompanyId);
        var partyControlAccountId = await AccountIdAsync(environment, scenario.CompanyId, "1030");
        var stockAccountId = await AccountIdAsync(environment, scenario.CompanyId, "5020");

        async Task<HttpResponseMessage> Post(object body) => await scenario.OwnerClient.PostAsJsonAsync(PaymentUrl, body);

        // amount zero or negative
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(VoucherBody(scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(VoucherBody(scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), -5))).StatusCode);
        // both boxes name a party: refused before the database
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(VoucherBody(scenario.FirstStoreId, PartyTarget(vendorId), PartyTarget(otherVendorId), 100))).StatusCode);
        // a box with neither an account nor a party, or with both
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(VoucherBody(scenario.FirstStoreId, new { accountId = (Guid?)null, partyId = (Guid?)null }, AccountTarget(cashAccountId), 100))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(VoucherBody(scenario.FirstStoreId, new { accountId = (Guid?)cashAccountId, partyId = (Guid?)vendorId }, AccountTarget(cashAccountId), 100))).StatusCode);
        // unknown payment mode
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(VoucherBody(scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 100, paymentMode: "BARTER"))).StatusCode);

        // database rules answer 422: the same account on both sides, the party control account without a party, a stock account
        var sameAccount = await Post(VoucherBody(scenario.FirstStoreId, AccountTarget(cashAccountId), AccountTarget(cashAccountId), 100));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sameAccount.StatusCode);
        var controlAccount = await Post(VoucherBody(scenario.FirstStoreId, AccountTarget(partyControlAccountId), AccountTarget(cashAccountId), 100));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, controlAccount.StatusCode);
        Assert.Equal(ErrorCodes.DatabaseRuleViolation, await ErrorCodeOfAsync(controlAccount));
        var stockAccount = await Post(VoucherBody(scenario.FirstStoreId, AccountTarget(stockAccountId), AccountTarget(cashAccountId), 100));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, stockAccount.StatusCode);

        // nothing was posted by all of those attempts
        Assert.Equal(0L, await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from acct.vouchers where company_id = $1", scenario.CompanyId));
    }

    [Fact]
    public async Task Voiding_reverses_the_entry_keeps_the_history_and_cannot_happen_twice()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var vendorId = await PartyTestHelpers.CreatePartyAsync(scenario.OwnerClient, "Vendor To Void", isCustomer: false, isVendor: true);
        var cashAccountId = await FirstStoreCashAccountIdAsync(environment, scenario.CompanyId);
        var voucher = await PostVoucherAsync(scenario.OwnerClient, PaymentUrl,
            VoucherBody(scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 20_000));
        var voucherId = voucher.GetProperty("id").GetGuid();
        var voidUrl = $"{VouchersUrl}/{voucherId}/void";

        // a reason is required
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PostAsJsonAsync(voidUrl, new { reason = "  " })).StatusCode);

        var voided = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, voidUrl, new { reason = "Wrong vendor" });
        Assert.Equal("VOID", voided.GetProperty("statusCode").GetString());
        Assert.Equal("Wrong vendor", voided.GetProperty("voidReason").GetString());
        Assert.Equal(JsonValueKind.String, voided.GetProperty("voidedAt").ValueKind);

        // nothing was deleted: two entries (the original and its reversal), four lines, balances back to zero
        Assert.Equal(2L, await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from acct.journal_entries where company_id = $1 and source_type like 'VOUCHER%'", scenario.CompanyId));
        Assert.Equal(4L, await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from acct.journal_lines where company_id = $1", scenario.CompanyId));
        Assert.Equal(0, await PartyNetBalanceAsync(environment, scenario.CompanyId, vendorId));
        Assert.Equal(0, await NetBalanceOfAccountAsync(environment, scenario.CompanyId, cashAccountId));
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));

        // the voucher is still readable, and a second void is a conflict
        var read = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{VouchersUrl}/{voucherId}"));
        Assert.Equal("VOID", read.GetProperty("statusCode").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.OwnerClient.PostAsJsonAsync(voidUrl, new { reason = "Again" })).StatusCode);
    }

    [Fact]
    public async Task A_retry_with_the_same_client_transaction_id_returns_the_first_voucher_and_posts_nothing_twice()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var vendorId = await PartyTestHelpers.CreatePartyAsync(scenario.OwnerClient, "Retrying Vendor", isCustomer: false, isVendor: true);
        var cashAccountId = await FirstStoreCashAccountIdAsync(environment, scenario.CompanyId);
        var clientTransactionId = Guid.NewGuid();
        var body = VoucherBody(scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 9_000, clientTransactionId: clientTransactionId);

        var first = await PostVoucherAsync(scenario.OwnerClient, PaymentUrl, body);
        var retry = await PostVoucherAsync(scenario.OwnerClient, PaymentUrl, body);

        Assert.Equal(first.GetProperty("id").GetGuid(), retry.GetProperty("id").GetGuid());
        Assert.Equal(1L, await environment.Database.QueryScalarAsOwnerAsync<long>("select count(*) from acct.vouchers where company_id = $1", scenario.CompanyId));
        Assert.Equal(1L, await environment.Database.QueryScalarAsOwnerAsync<long>("select count(*) from acct.journal_entries where company_id = $1", scenario.CompanyId));
        Assert.Equal(9_000, await PartyNetBalanceAsync(environment, scenario.CompanyId, vendorId));
    }

    [Fact]
    public async Task A_closed_month_refuses_vouchers_dated_in_it_and_reopening_allows_them_again()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var vendorId = await PartyTestHelpers.CreatePartyAsync(scenario.OwnerClient, "Vendor In A Closed Month", isCustomer: false, isVendor: true);
        var cashAccountId = await FirstStoreCashAccountIdAsync(environment, scenario.CompanyId);
        var periodsUrl = TestScenarioBuilder.Url(ApiRoutes.AccountingPeriods.Root);
        var body = VoucherBody(scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 1_500, voucherDate: "2026-03-15");

        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, $"{periodsUrl}/{ApiRoutes.AccountingPeriods.Close}", new { periodStart = "2026-03-01" });

        var refused = await scenario.OwnerClient.PostAsJsonAsync(PaymentUrl, body);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal(ErrorCodes.DatabaseRuleViolation, await ErrorCodeOfAsync(refused));

        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, $"{periodsUrl}/{ApiRoutes.AccountingPeriods.Reopen}", new { periodStart = "2026-03-01" });
        var accepted = await PostVoucherAsync(scenario.OwnerClient, PaymentUrl, body);
        Assert.Equal("POSTED", accepted.GetProperty("statusCode").GetString());
    }

    [Fact]
    public async Task Allocations_settle_purchase_invoices_and_voiding_the_voucher_releases_them()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var vendorId = await PartyTestHelpers.CreatePartyAsync(scenario.OwnerClient, "Vendor With Bills", isCustomer: false, isVendor: true);
        var otherVendorId = await PartyTestHelpers.CreatePartyAsync(scenario.OwnerClient, "Another Vendor", isCustomer: false, isVendor: true);
        var cashAccountId = await FirstStoreCashAccountIdAsync(environment, scenario.CompanyId);
        var invoiceId = await CreatePurchaseInvoiceAsync(environment, scenario.CompanyId, scenario.FirstStoreId, vendorId, 50_000);
        var otherInvoiceId = await CreatePurchaseInvoiceAsync(environment, scenario.CompanyId, scenario.FirstStoreId, otherVendorId, 10_000, invoiceNumber: 2);

        var voucher = await PostVoucherAsync(scenario.OwnerClient, PaymentUrl, VoucherBody(
            scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 40_000,
            allocations: new[] { new { invoiceId, amount = 30_000L } }));
        Assert.Equal(20_000, await PurchaseInvoiceRemainingAsync(environment, scenario.CompanyId, invoiceId));
        var allocation = voucher.GetProperty("allocations").EnumerateArray().Single();
        Assert.Equal("PURCHASE", allocation.GetProperty("invoiceKindCode").GetString());
        Assert.Equal(30_000, allocation.GetProperty("amount").GetInt64());

        // more than the remaining 20,000, an invoice of another vendor, allocations above the voucher amount, and a unknown invoice
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await scenario.OwnerClient.PostAsJsonAsync(PaymentUrl, VoucherBody(
            scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 40_000,
            allocations: new[] { new { invoiceId, amount = 25_000L } }))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await scenario.OwnerClient.PostAsJsonAsync(PaymentUrl, VoucherBody(
            scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 5_000,
            allocations: new[] { new { invoiceId = otherInvoiceId, amount = 5_000L } }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PostAsJsonAsync(PaymentUrl, VoucherBody(
            scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 5_000,
            allocations: new[] { new { invoiceId, amount = 6_000L } }))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await scenario.OwnerClient.PostAsJsonAsync(PaymentUrl, VoucherBody(
            scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 5_000,
            allocations: new[] { new { invoiceId = Guid.NewGuid(), amount = 5_000L } }))).StatusCode);
        // a journal voucher cannot settle invoices
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PostAsJsonAsync(JournalUrl, VoucherBody(
            scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 5_000,
            allocations: new[] { new { invoiceId, amount = 1_000L } }))).StatusCode);
        Assert.Equal(20_000, await PurchaseInvoiceRemainingAsync(environment, scenario.CompanyId, invoiceId));

        // voiding the payment gives the 30,000 back to the invoice
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, $"{VouchersUrl}/{voucher.GetProperty("id").GetGuid()}/void", new { reason = "Cheque bounced" });
        Assert.Equal(50_000, await PurchaseInvoiceRemainingAsync(environment, scenario.CompanyId, invoiceId));
    }

    [Fact]
    public async Task Search_by_number_and_the_report_count_only_posted_vouchers_in_the_total()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var vendorId = await PartyTestHelpers.CreatePartyAsync(scenario.OwnerClient, "Vendor For Reports", isCustomer: false, isVendor: true);
        var cashAccountId = await FirstStoreCashAccountIdAsync(environment, scenario.CompanyId);
        var first = await PostVoucherAsync(scenario.OwnerClient, PaymentUrl, VoucherBody(scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 10_000, description: "January rent top-up"));
        var second = await PostVoucherAsync(scenario.OwnerClient, PaymentUrl, VoucherBody(scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 4_000));
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, $"{VouchersUrl}/{second.GetProperty("id").GetGuid()}/void", new { reason = "Entered twice" });

        // by type and number (View Voucher)
        var byNumber = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(
            $"{VouchersUrl}/{ApiRoutes.Vouchers.ByNumber}?voucherTypeCode=PAYMENT&voucherNumber=1&storeId={scenario.FirstStoreId}"));
        Assert.Equal(first.GetProperty("id").GetGuid(), byNumber.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.OwnerClient.GetAsync(
            $"{VouchersUrl}/{ApiRoutes.Vouchers.ByNumber}?voucherTypeCode=PAYMENT&voucherNumber=99&storeId={scenario.FirstStoreId}")).StatusCode);

        // list with a text search and the report
        var list = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{VouchersUrl}?voucherTypeCode=PAYMENT&searchText=top-up"));
        Assert.Equal(1, list.GetProperty("totalCount").GetInt64());
        var report = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(
            $"{VouchersUrl}/{ApiRoutes.Vouchers.Report}?voucherTypeCode=PAYMENT&fromDate=2026-11-01&toDate=2026-11-30"));
        Assert.Equal(2, report.GetProperty("rows").GetArrayLength());
        Assert.Equal(1, report.GetProperty("postedVoucherCount").GetInt64());
        Assert.Equal(1, report.GetProperty("voidVoucherCount").GetInt64());
        Assert.Equal(10_000, report.GetProperty("totalAmount").GetInt64());
        Assert.False(report.GetProperty("isTruncated").GetBoolean());
        // the report needs both dates
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.GetAsync($"{VouchersUrl}/{ApiRoutes.Vouchers.Report}?voucherTypeCode=PAYMENT")).StatusCode);
    }

    [Fact]
    public async Task Update_description_changes_only_the_narration_and_a_stale_row_version_is_a_conflict()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var vendorId = await PartyTestHelpers.CreatePartyAsync(scenario.OwnerClient, "Vendor For Narration", isCustomer: false, isVendor: true);
        var cashAccountId = await FirstStoreCashAccountIdAsync(environment, scenario.CompanyId);
        var voucher = await PostVoucherAsync(scenario.OwnerClient, PaymentUrl, VoucherBody(scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 3_000, description: "Old text"));
        var url = $"{VouchersUrl}/{voucher.GetProperty("id").GetGuid()}/description";
        var rowVersion = voucher.GetProperty("rowVersion").GetInt64();

        var response = await scenario.OwnerClient.PutAsJsonAsync(url, new { description = "New text", expectedRowVersion = rowVersion });
        var updated = await OwnerCompanyScenario.ReadJsonAsync(response);
        Assert.Equal("New text", updated.GetProperty("description").GetString());
        Assert.Equal(3_000, updated.GetProperty("amount").GetInt64());
        Assert.True(updated.GetProperty("rowVersion").GetInt64() > rowVersion);
        // the posted journal lines never change
        Assert.Equal(2L, await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from acct.journal_lines where company_id = $1 and description = 'Old text'", scenario.CompanyId));

        var stale = await scenario.OwnerClient.PutAsJsonAsync(url, new { description = "Lost update", expectedRowVersion = rowVersion });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(ErrorCodes.ConcurrentUpdate, await ErrorCodeOfAsync(stale));
    }

    [Fact]
    public async Task Each_voucher_screen_has_its_own_permission_and_posting_also_needs_the_legacy_code()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var vendorId = await PartyTestHelpers.CreatePartyAsync(scenario.OwnerClient, "Vendor For Permissions", isCustomer: false, isVendor: true);
        var cashAccountId = await FirstStoreCashAccountIdAsync(environment, scenario.CompanyId);
        var body = VoucherBody(scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 500);

        var nothing = await scenario.SignInManagerWithPermissionsAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.PostAsJsonAsync(PaymentUrl, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.GetAsync($"{VouchersUrl}/{ApiRoutes.Vouchers.PaymentEntryContext}?storeId={scenario.FirstStoreId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.GetAsync(VouchersUrl)).StatusCode);

        // the Receipt screen permission does not open the Payment screen
        var receiptOnly = await scenario.SignInManagerWithPermissionsAsync(
            PermissionCodes.Voucher.ReceiptVoucher.Add, PermissionCodes.RestrictedOperations.Accounting.VoucherPost);
        Assert.Equal(HttpStatusCode.Forbidden, (await receiptOnly.PostAsJsonAsync(PaymentUrl, body)).StatusCode);

        // the screen permission alone is not enough: the legacy VOUCHER_POST code is checked too
        var withoutLegacy = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Voucher.PaymentVoucher.Add);
        Assert.Equal(HttpStatusCode.Forbidden, (await withoutLegacy.PostAsJsonAsync(PaymentUrl, body)).StatusCode);

        var cashier = await scenario.SignInManagerWithPermissionsAsync(
            PermissionCodes.Voucher.PaymentVoucher.Add, PermissionCodes.RestrictedOperations.Accounting.VoucherPost);
        var posted = await cashier.PostAsJsonAsync(PaymentUrl, body);
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);

        // viewing, narration edits and voiding are separate permissions as well
        var voucherId = (await OwnerCompanyScenario.ReadJsonAsync(posted)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync($"{VouchersUrl}/{voucherId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync($"{VouchersUrl}/{voucherId}/void", new { reason = "x" })).StatusCode);
        var viewer = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Voucher.ViewVoucher.View);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{VouchersUrl}/{voucherId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"{VouchersUrl}/{voucherId}/void", new { reason = "x" })).StatusCode);
        var voidOnlyScreen = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Voucher.ViewVoucher.Delete);
        Assert.Equal(HttpStatusCode.Forbidden, (await voidOnlyScreen.PostAsJsonAsync($"{VouchersUrl}/{voucherId}/void", new { reason = "x" })).StatusCode);
    }

    [Fact]
    public async Task A_platform_administrator_cannot_use_the_voucher_endpoints()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.PlatformClient.GetAsync(VouchersUrl)).StatusCode);
        using var anonymousClient = environment.ApiFactory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.GetAsync(VouchersUrl)).StatusCode);
    }

    [Fact]
    public async Task Vouchers_are_isolated_between_companies_and_between_stores()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        var vendorId = await PartyTestHelpers.CreatePartyAsync(scenario.OwnerClient, "Vendor For Isolation", isCustomer: false, isVendor: true);
        var cashAccountId = await FirstStoreCashAccountIdAsync(environment, scenario.CompanyId);
        var firstStoreVoucher = await PostVoucherAsync(scenario.OwnerClient, PaymentUrl, VoucherBody(scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 800));
        var voucherId = firstStoreVoucher.GetProperty("id").GetGuid();

        // another company never sees it
        var otherCompany = await OwnerCompanyScenario.CreateAsync(environment);
        Assert.Equal(HttpStatusCode.NotFound, (await otherCompany.OwnerClient.GetAsync($"{VouchersUrl}/{voucherId}")).StatusCode);
        Assert.Equal(0, (await OwnerCompanyScenario.ReadJsonAsync(await otherCompany.OwnerClient.GetAsync(VouchersUrl))).GetProperty("totalCount").GetInt64());
        // and cannot post against this company's account or party
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await otherCompany.OwnerClient.PostAsJsonAsync(PaymentUrl,
            VoucherBody(otherCompany.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 100))).StatusCode);

        // a manager of the second store only: cannot read the first store's voucher and cannot post into it
        var secondStoreManager = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [secondStoreId],
            PermissionCodes.Voucher.ViewVoucher.View, PermissionCodes.Voucher.PaymentVoucher.Add, PermissionCodes.RestrictedOperations.Accounting.VoucherPost);
        Assert.Equal(HttpStatusCode.NotFound, (await secondStoreManager.GetAsync($"{VouchersUrl}/{voucherId}")).StatusCode);
        Assert.Equal(0, (await OwnerCompanyScenario.ReadJsonAsync(await secondStoreManager.GetAsync(VouchersUrl))).GetProperty("totalCount").GetInt64());
        Assert.Equal(HttpStatusCode.Forbidden, (await secondStoreManager.GetAsync($"{VouchersUrl}?storeId={scenario.FirstStoreId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await secondStoreManager.PostAsJsonAsync(PaymentUrl,
            VoucherBody(scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 100))).StatusCode);
        // the head office is reachable only by people who may see every store
        Assert.Equal(HttpStatusCode.Forbidden, (await secondStoreManager.PostAsJsonAsync(PaymentUrl,
            VoucherBody(Guid.Empty, PartyTarget(vendorId), AccountTarget(cashAccountId), 100))).StatusCode);

        // the owner sees both stores' vouchers
        await PostVoucherAsync(scenario.OwnerClient, PaymentUrl, VoucherBody(secondStoreId, PartyTarget(vendorId), AccountTarget(await AccountIdAsync(environment, scenario.CompanyId, "CASH-S2")), 900));
        Assert.Equal(2, (await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(VouchersUrl))).GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task Target_lookups_return_accounts_and_parties_with_balances_but_never_stock_or_the_party_control_account()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var vendorId = await PartyTestHelpers.CreatePartyAsync(scenario.OwnerClient, "Searchable Vendor Name", isCustomer: false, isVendor: true);
        var cashAccountId = await FirstStoreCashAccountIdAsync(environment, scenario.CompanyId);
        await PostVoucherAsync(scenario.OwnerClient, PaymentUrl, VoucherBody(scenario.FirstStoreId, PartyTarget(vendorId), AccountTarget(cashAccountId), 2_500));

        var accounts = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(
            $"{VouchersUrl}/{ApiRoutes.Vouchers.PaymentTargets}?kindCode=ACCOUNT"));
        var accountCodes = accounts.EnumerateArray().Select(option => option.GetProperty("code").GetString()!).ToList();
        Assert.Contains("CASH-S1", accountCodes);
        Assert.DoesNotContain("1030", accountCodes);   // parties are chosen as parties
        Assert.DoesNotContain("5020", accountCodes);   // stock is posted by stock documents
        var cash = accounts.EnumerateArray().Single(option => option.GetProperty("code").GetString() == "CASH-S1");
        Assert.Equal(2_500, cash.GetProperty("balanceAmount").GetInt64());
        Assert.Equal("CREDIT", cash.GetProperty("balanceSideCode").GetString());

        var parties = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(
            $"{VouchersUrl}/{ApiRoutes.Vouchers.PaymentTargets}?kindCode=PARTY&partyTypeCode=VENDOR&searchText=searchable"));
        var vendor = parties.EnumerateArray().Single();
        Assert.Equal(2_500, vendor.GetProperty("balanceAmount").GetInt64());
        Assert.Equal("DEBIT", vendor.GetProperty("balanceSideCode").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.GetAsync($"{VouchersUrl}/{ApiRoutes.Vouchers.PaymentTargets}?kindCode=NONSENSE")).StatusCode);
    }
}
