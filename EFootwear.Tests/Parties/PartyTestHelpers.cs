using System.Net.Http.Json;
using E_Footwear_Api.Routing;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;

namespace EFootwear.Tests.Parties;

/// <summary>Builders and database helpers shared by the party, ledger, promise, cash and opening-balance tests.</summary>
public static class PartyTestHelpers
{
    public static readonly Guid HeadOfficeStoreId = Guid.Empty;
    public static readonly string PartiesUrl = TestScenarioBuilder.Url(ApiRoutes.Parties.Root);

    /// <summary>A complete request body for creating or updating a party (every field the API reads). rowVersion is only used by updates.</summary>
    public static object PartyBody(
        string name,
        bool isCustomer = true,
        bool isVendor = false,
        bool isStaff = false,
        string? cnic = null,
        Guid? cityId = null,
        string? primaryPhone = null,
        string? email = null,
        Guid? staffStoreId = null,
        string? designation = null,
        long? creditLimit = null,
        string statusCode = "ACTIVE",
        object? openingBalance = null,
        string? fatherNameUrdu = null,
        string? addressUrdu = null,
        string? notes = null,
        long? rowVersion = null) => new
    {
        name,
        nameUrdu = (string?)null,
        fatherName = (string?)null,
        fatherNameUrdu,
        cnic,
        cityId,
        primaryPhone,
        secondaryPhone = (string?)null,
        email,
        address = (string?)null,
        addressUrdu,
        notes,
        isCustomer,
        isVendor,
        isStaff,
        creditLimit,
        statusCode,
        designation,
        isManager = false,
        staffStoreId,
        openingBalance,
        rowVersion,
    };

    public static async Task<Guid> CreatePartyAsync(HttpClient client, string name, bool isCustomer = true, bool isVendor = false) =>
        (await OwnerCompanyScenario.PostJsonAsync(client, PartiesUrl, PartyBody(name, isCustomer, isVendor))).GetProperty("id").GetGuid();

    public static async Task<System.Text.Json.JsonElement> GetPartyAsync(HttpClient client, Guid partyId) =>
        await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync($"{PartiesUrl}/{partyId}"));

    /// <summary>
    /// Posts a balanced journal entry that touches a party, straight into the database (the sales, purchase and voucher modules that
    /// normally post such entries are built separately). The party line is a debit or a credit; the other line goes to the sales account.
    /// </summary>
    public static async Task PostLedgerEntryAsync(ApiTestEnvironment environment, Guid companyId, Guid partyId, DateOnly entryDate, long debit, long credit) =>
        await environment.Database.ExecuteAsOwnerAsync("""
            with entry as (
                insert into acct.journal_entries (company_id, entry_date, store_id, source_type, description)
                values ($1, $2, '00000000-0000-0000-0000-000000000000'::uuid, 'TEST_ENTRY', 'Test ledger entry')
                returning id)
            insert into acct.journal_lines (company_id, entry_id, entry_date, line_no, store_id, account_id, party_id, debit, credit, description)
            select $1, entry.id, $2, 1, '00000000-0000-0000-0000-000000000000'::uuid, acct.role_account($1, 'PARTY_CONTROL'), $3, $4, $5, 'Party line' from entry
            union all
            select $1, entry.id, $2, 2, '00000000-0000-0000-0000-000000000000'::uuid, acct.role_account($1, 'SALES'), null, $5, $4, 'Balancing line' from entry
            """, companyId, entryDate, partyId, debit, credit);

    /// <summary>Posts a balanced entry that debits the cash accounts in a store (and credits sales), so the "computer cash" of the store rises.</summary>
    public static async Task PostCashReceivedAsync(ApiTestEnvironment environment, Guid companyId, Guid storeId, DateOnly entryDate, long amount) =>
        await environment.Database.ExecuteAsOwnerAsync("""
            with entry as (
                insert into acct.journal_entries (company_id, entry_date, store_id, source_type, description)
                values ($1, $2, $3, 'TEST_ENTRY', 'Test cash received')
                returning id)
            insert into acct.journal_lines (company_id, entry_id, entry_date, line_no, store_id, account_id, party_id, debit, credit, description)
            select $1, entry.id, $2, 1, $3, acct.role_account($1, 'CASH'), null::uuid, $4, 0, 'Cash' from entry
            union all
            select $1, entry.id, $2, 2, $3, acct.role_account($1, 'SALES'), null::uuid, 0, $4, 'Sales' from entry
            """, companyId, entryDate, storeId, amount);

    public static async Task<long> JournalDifferenceAsync(ApiTestEnvironment environment, Guid companyId) =>
        await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select (coalesce(sum(debit), 0) - coalesce(sum(credit), 0))::bigint from acct.journal_lines where company_id = $1", companyId);

    public static async Task<HttpResponseMessage> PutJsonAsync(HttpClient client, string url, object body) => await client.PutAsJsonAsync(url, body);
}
