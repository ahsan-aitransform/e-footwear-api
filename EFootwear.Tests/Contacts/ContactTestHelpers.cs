using System.Net.Http.Json;
using E_Footwear_Api.Routing;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;

namespace EFootwear.Tests.Contacts;

/// <summary>Builders and database helpers shared by the customer, vendor, staff, ledger, promise, cash and opening-balance tests.</summary>
public static class ContactTestHelpers
{
    public static readonly Guid HeadOfficeStoreId = Guid.Empty;
    public static readonly string CustomersUrl = TestScenarioBuilder.Url(ApiRoutes.Customers.Root);
    public static readonly string VendorsUrl = TestScenarioBuilder.Url(ApiRoutes.Vendors.Root);
    public static readonly string StaffMembersUrl = TestScenarioBuilder.Url(ApiRoutes.StaffMembers.Root);

    /// <summary>A complete request body for creating or updating a customer (every field the API reads). rowVersion is only used by updates.</summary>
    public static object CustomerBody(
        string name,
        string? phone = "0300 1234567",
        string? email = null,
        string? birthDate = null,
        Guid? cityId = null,
        string statusCode = "ACTIVE",
        object? openingBalance = null,
        long? rowVersion = null,
        string? cnic = null,
        string? customerTypeCode = null) => new { name, phone, email, birthDate, cityId, statusCode, openingBalance, rowVersion, cnic, customerTypeCode };

    public static object VendorBody(
        string name,
        string? phone = "0301 7654321",
        string? email = null,
        string? birthDate = null,
        Guid? cityId = null,
        long? creditLimit = null,
        string statusCode = "ACTIVE",
        object? openingBalance = null,
        long? rowVersion = null) => new { name, phone, email, birthDate, cityId, creditLimit, statusCode, openingBalance, rowVersion };

    /// <summary>A staff member body. storeId is read on create only: an existing staff member changes store through a transfer.</summary>
    public static object StaffMemberBody(
        string name,
        Guid? storeId,
        string? phone = "0302 1112223",
        string? email = null,
        string? birthDate = null,
        Guid? cityId = null,
        string? designation = null,
        bool isManager = false,
        string statusCode = "ACTIVE",
        object? openingBalance = null,
        long? rowVersion = null) => new { name, phone, email, birthDate, cityId, designation, isManager, storeId, statusCode, openingBalance, rowVersion };

    public static async Task<Guid> CreateCustomerAsync(HttpClient client, string name) =>
        (await OwnerCompanyScenario.PostJsonAsync(client, CustomersUrl, CustomerBody(name))).GetProperty("id").GetGuid();

    public static async Task<Guid> CreateVendorAsync(HttpClient client, string name, long? creditLimit = null) =>
        (await OwnerCompanyScenario.PostJsonAsync(client, VendorsUrl, VendorBody(name, creditLimit: creditLimit))).GetProperty("id").GetGuid();

    public static async Task<Guid> CreateStaffMemberAsync(HttpClient client, string name, Guid storeId, string? designation = null) =>
        (await OwnerCompanyScenario.PostJsonAsync(client, StaffMembersUrl, StaffMemberBody(name, storeId, designation: designation))).GetProperty("id").GetGuid();

    public static async Task<System.Text.Json.JsonElement> GetCustomerAsync(HttpClient client, Guid customerId) =>
        await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync($"{CustomersUrl}/{customerId}"));

    public static async Task<System.Text.Json.JsonElement> GetVendorAsync(HttpClient client, Guid vendorId) =>
        await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync($"{VendorsUrl}/{vendorId}"));

    public static async Task<System.Text.Json.JsonElement> GetStaffMemberAsync(HttpClient client, Guid staffId) =>
        await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync($"{StaffMembersUrl}/{staffId}"));

    public static Task PostCustomerLedgerEntryAsync(ApiTestEnvironment environment, Guid companyId, Guid customerId, DateOnly entryDate, long debit, long credit) =>
        PostLedgerEntryAsync(environment, companyId, "CUSTOMER", customerId, entryDate, debit, credit);

    public static Task PostVendorLedgerEntryAsync(ApiTestEnvironment environment, Guid companyId, Guid vendorId, DateOnly entryDate, long debit, long credit) =>
        PostLedgerEntryAsync(environment, companyId, "VENDOR", vendorId, entryDate, debit, credit);

    public static Task PostStaffLedgerEntryAsync(ApiTestEnvironment environment, Guid companyId, Guid staffId, DateOnly entryDate, long debit, long credit) =>
        PostLedgerEntryAsync(environment, companyId, "STAFF", staffId, entryDate, debit, credit);

    /// <summary>
    /// Posts a balanced journal entry that touches a customer, vendor or staff member, straight into the database (the sales, purchase and voucher
    /// modules that normally post such entries are built separately). The contact line is a debit or a credit; the other line goes to the sales account.
    /// </summary>
    public static async Task PostLedgerEntryAsync(ApiTestEnvironment environment, Guid companyId, string kindCode, Guid contactId, DateOnly entryDate, long debit, long credit) =>
        await environment.Database.ExecuteAsOwnerAsync("""
            with entry as (
                insert into acct.journal_entries (company_id, entry_date, store_id, source_type, description)
                values ($1, $2, '00000000-0000-0000-0000-000000000000'::uuid, 'TEST_ENTRY', 'Test ledger entry')
                returning id)
            insert into acct.journal_lines (company_id, entry_id, entry_date, line_no, store_id, account_id, customer_id, vendor_id, staff_id, debit, credit, description)
            select $1, entry.id, $2, 1, '00000000-0000-0000-0000-000000000000'::uuid, acct.role_account($1, 'SUBLEDGER_CONTROL'),
                   case when $6::text = 'CUSTOMER' then $3::uuid end, case when $6::text = 'VENDOR' then $3::uuid end, case when $6::text = 'STAFF' then $3::uuid end,
                   $4, $5, 'Contact line' from entry
            union all
            select $1, entry.id, $2, 2, '00000000-0000-0000-0000-000000000000'::uuid, acct.role_account($1, 'SALES'), null::uuid, null::uuid, null::uuid, $5, $4, 'Balancing line' from entry
            """, companyId, entryDate, contactId, debit, credit, kindCode);

    /// <summary>Posts a balanced entry that debits the cash accounts in a store (and credits sales), so the "computer cash" of the store rises.</summary>
    public static async Task PostCashReceivedAsync(ApiTestEnvironment environment, Guid companyId, Guid storeId, DateOnly entryDate, long amount) =>
        await environment.Database.ExecuteAsOwnerAsync("""
            with entry as (
                insert into acct.journal_entries (company_id, entry_date, store_id, source_type, description)
                values ($1, $2, $3, 'TEST_ENTRY', 'Test cash received')
                returning id)
            insert into acct.journal_lines (company_id, entry_id, entry_date, line_no, store_id, account_id, debit, credit, description)
            select $1, entry.id, $2, 1, $3, acct.role_account($1, 'CASH'), $4, 0, 'Cash' from entry
            union all
            select $1, entry.id, $2, 2, $3, acct.role_account($1, 'SALES'), 0, $4, 'Sales' from entry
            """, companyId, entryDate, storeId, amount);

    public static async Task<long> JournalDifferenceAsync(ApiTestEnvironment environment, Guid companyId) =>
        await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select (coalesce(sum(debit), 0) - coalesce(sum(credit), 0))::bigint from acct.journal_lines where company_id = $1", companyId);

    public static async Task<HttpResponseMessage> PutJsonAsync(HttpClient client, string url, object body) => await client.PutAsJsonAsync(url, body);
}
