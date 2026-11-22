using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;

namespace EFootwear.Tests.Accounting;

/// <summary>Shared helpers of the voucher, account report, period and cash tests: request bodies, account lookups and a minimal purchase invoice.</summary>
public static class VoucherTestHelpers
{
    public static readonly string VouchersUrl = TestScenarioBuilder.Url(ApiRoutes.Vouchers.Root);
    public const string TestVoucherDate = "2026-11-10";

    public static string PaymentUrl => $"{VouchersUrl}/{ApiRoutes.Vouchers.Payment}";
    public static string ReceiptUrl => $"{VouchersUrl}/{ApiRoutes.Vouchers.Receipt}";
    public static string ExpenseUrl => $"{VouchersUrl}/{ApiRoutes.Vouchers.Expense}";
    public static string JournalUrl => $"{VouchersUrl}/{ApiRoutes.Vouchers.Journal}";

    public static object AccountTarget(Guid accountId) => new { accountId = (Guid?)accountId, partyId = (Guid?)null };

    public static object PartyTarget(Guid partyId) => new { accountId = (Guid?)null, partyId = (Guid?)partyId };

    /// <summary>A voucher request. <c>debit</c> is the "To" box (Dr), <c>credit</c> the "From" box (Cr).</summary>
    public static object VoucherBody(
        Guid storeId, object debit, object credit, long amount, string voucherDate = TestVoucherDate, string? description = "Test voucher",
        Guid? clientTransactionId = null, object? allocations = null, string? paymentMode = "CASH", string? referenceNumber = null) =>
        new
        {
            storeId,
            voucherDate,
            debitTarget = debit,
            creditTarget = credit,
            amount,
            paymentMode,
            referenceNumber,
            description,
            allocations,
            clientTransactionId,
        };

    public static async Task<JsonElement> PostVoucherAsync(HttpClient client, string url, object body) =>
        await OwnerCompanyScenario.PostJsonAsync(client, url, body);

    public static async Task<Guid> AccountIdAsync(ApiTestEnvironment environment, Guid companyId, string accountCode) =>
        await environment.Database.QueryScalarAsOwnerAsync<Guid>(
            "select id from acct.accounts where company_id = $1 and code = $2", companyId, accountCode);

    /// <summary>The cash account of the company's first store (created by the store trigger).</summary>
    public static Task<Guid> FirstStoreCashAccountIdAsync(ApiTestEnvironment environment, Guid companyId) =>
        AccountIdAsync(environment, companyId, "CASH-S1");

    public static async Task<long> JournalDifferenceAsync(ApiTestEnvironment environment, Guid companyId) =>
        await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select coalesce(sum(debit) - sum(credit), 0)::bigint from acct.journal_lines where company_id = $1", companyId);

    public static async Task<long> NetBalanceOfAccountAsync(ApiTestEnvironment environment, Guid companyId, Guid accountId) =>
        await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select coalesce(sum(debit - credit), 0)::bigint from acct.journal_lines where company_id = $1 and account_id = $2", companyId, accountId);

    public static async Task<long> PartyNetBalanceAsync(ApiTestEnvironment environment, Guid companyId, Guid partyId) =>
        await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select coalesce(sum(debit - credit), 0)::bigint from acct.journal_lines where company_id = $1 and party_id = $2", companyId, partyId);

    /// <summary>
    /// A posted purchase invoice of one vendor, made directly in the database (the purchasing module builds them through its own screens).
    /// The journal entry is inserted first and the invoice points at it, which is exactly the state the posting trigger leaves behind.
    /// </summary>
    public static async Task<Guid> CreatePurchaseInvoiceAsync(
        ApiTestEnvironment environment, Guid companyId, Guid storeId, Guid vendorPartyId, long netAmount, long invoiceNumber = 1)
    {
        var entryId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        await environment.Database.ExecuteAsOwnerAsync("""
            with entry as (
                insert into acct.journal_entries (company_id, id, entry_date, store_id, source_type, source_id, description)
                values ($1, $2, date '2026-11-05', $3, 'PURCHASE', $4, 'Purchase bill for a test') returning id
            ), stock_line as (
                insert into acct.journal_lines (company_id, entry_id, entry_date, line_no, store_id, account_id, debit, credit)
                select $1, $2, date '2026-11-05', 1, $3, acct.role_account($1, 'STOCK'), $5, 0 from entry
            ), vendor_line as (
                insert into acct.journal_lines (company_id, entry_id, entry_date, line_no, store_id, account_id, party_id, debit, credit)
                select $1, $2, date '2026-11-05', 2, $3, acct.role_account($1, 'PARTY_CONTROL'), $6, 0, $5 from entry
            )
            insert into purch.invoices (company_id, id, store_id, doc_type, invoice_no, vendor_party_id, bill_date, gross_amount, net_amount, status, journal_entry_id)
            select $1, $4, $3, 'PURCHASE', $7, $6, date '2026-11-05', $5, $5, 'POSTED', $2 from entry
            """, companyId, entryId, storeId, invoiceId, netAmount, vendorPartyId, invoiceNumber);
        return invoiceId;
    }

    public static async Task<long> PurchaseInvoiceRemainingAsync(ApiTestEnvironment environment, Guid companyId, Guid invoiceId) =>
        await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select remaining_amount::bigint from purch.v_invoice_balance where company_id = $1 and invoice_id = $2", companyId, invoiceId);

    public static async Task<string> ErrorCodeOfAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString()!;
}
