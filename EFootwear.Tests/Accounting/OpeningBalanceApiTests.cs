using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using EFootwear.Tests.Parties;

namespace EFootwear.Tests.Accounting;

/// <summary>
/// The Opening Balance screen (docx 2.18). Saving a balance posts one balanced journal entry against the Opening balance equity
/// account; saving again replaces it by reversal and repost, so the journal stays append-only and always balanced.
/// </summary>
[Collection(ApiIntegrationCollection.Name)]
public class OpeningBalanceApiTests(ApiTestEnvironment environment)
{
    private static readonly string OpeningBalancesUrl = TestScenarioBuilder.Url(ApiRoutes.OpeningBalances.Root);

    private static string SheetUrl(Guid storeId) => $"{OpeningBalancesUrl}?storeId={storeId}";

    private static async Task<JsonElement> GetSheetAsync(HttpClient client, Guid storeId) =>
        await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(SheetUrl(storeId)));

    private static JsonElement FindRowByCode(JsonElement sheet, string code) =>
        sheet.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("code").GetString() == code);

    private static object AccountLine(Guid accountId, long debit, long credit) =>
        new { accountId = (Guid?)accountId, partyId = (Guid?)null, debitAmount = debit, creditAmount = credit };

    private static object PartyLine(Guid partyId, long debit, long credit) =>
        new { accountId = (Guid?)null, partyId = (Guid?)partyId, debitAmount = debit, creditAmount = credit };

    private static object Save(Guid storeId, string entryDate, params object[] lines) => new { storeId, entryDate, lines };

    private async Task<long> NetBalanceOfAccountAsync(Guid companyId, Guid accountId) =>
        await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select coalesce(sum(debit - credit), 0)::bigint from acct.journal_lines where company_id = $1 and account_id = $2", companyId, accountId);

    [Fact]
    public async Task The_head_office_sheet_lists_accounts_and_parties_but_not_the_accounts_the_system_manages()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var sheet = await GetSheetAsync(scenario.OwnerClient, Guid.Empty);
        var codes = sheet.GetProperty("rows").EnumerateArray().Select(row => row.GetProperty("code").GetString()!).ToList();

        Assert.Contains("1010", codes);     // cash in hand (head office)
        Assert.Contains("1020", codes);     // bank
        Assert.Contains("3010", codes);     // capital
        Assert.Contains("WALKIN", codes);   // the default customer is a party row
        Assert.DoesNotContain("5020", codes);   // stock on hand: opening stock belongs to inventory
        Assert.DoesNotContain("5010", codes);   // stock in transit
        Assert.DoesNotContain("3020", codes);   // opening balance equity itself
        Assert.DoesNotContain("1030", codes);   // the party control account: parties have their own rows
        Assert.Equal(0, sheet.GetProperty("totalDebitAmount").GetInt64());
    }

    [Fact]
    public async Task A_store_sheet_lists_that_stores_own_accounts_only()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var sheet = await GetSheetAsync(scenario.OwnerClient, scenario.FirstStoreId);
        var codes = sheet.GetProperty("rows").EnumerateArray().Select(row => row.GetProperty("code").GetString()!).ToList();

        Assert.Contains("CASH-S1", codes);
        Assert.DoesNotContain("1010", codes);
        Assert.DoesNotContain("STOCK-S1", codes);   // the store's stock account is managed through inventory
        Assert.DoesNotContain("WALKIN", codes);
    }

    [Fact]
    public async Task Saving_posts_one_balanced_entry_and_saving_again_replaces_it_by_reversal_and_repost()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var cashAccountId = FindRowByCode(await GetSheetAsync(scenario.OwnerClient, Guid.Empty), "1010").GetProperty("accountId").GetGuid();

        var first = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.PutAsJsonAsync(OpeningBalancesUrl, Save(Guid.Empty, "2026-01-01", AccountLine(cashAccountId, 100_000, 0))));
        Assert.Equal(1, first.GetProperty("postedCount").GetInt32());
        Assert.Equal(100_000, await NetBalanceOfAccountAsync(scenario.CompanyId, cashAccountId));
        Assert.Equal(0, await PartyTestHelpers.JournalDifferenceAsync(environment, scenario.CompanyId));

        // saving the same screen twice posts nothing twice
        var again = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.PutAsJsonAsync(OpeningBalancesUrl, Save(Guid.Empty, "2026-01-01", AccountLine(cashAccountId, 100_000, 0))));
        Assert.Equal(0, again.GetProperty("postedCount").GetInt32());
        Assert.Equal(1, again.GetProperty("unchangedCount").GetInt32());
        Assert.Equal(1L, await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from acct.journal_entries where company_id = $1 and source_type like 'OPENING%'", scenario.CompanyId));

        // a changed amount reverses the old entry and posts a new one; the old entry is still there
        var changed = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.PutAsJsonAsync(OpeningBalancesUrl, Save(Guid.Empty, "2026-01-01", AccountLine(cashAccountId, 80_000, 0))));
        Assert.Equal(1, changed.GetProperty("postedCount").GetInt32());
        Assert.Equal(3L, await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from acct.journal_entries where company_id = $1 and source_type like 'OPENING%'", scenario.CompanyId));
        Assert.Equal(1L, await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from acct.journal_entries where company_id = $1 and source_type = 'OPENING_REVERSAL' and reversal_of is not null", scenario.CompanyId));
        Assert.Equal(80_000, await NetBalanceOfAccountAsync(scenario.CompanyId, cashAccountId));
        Assert.Equal(80_000, FindRowByCode(await GetSheetAsync(scenario.OwnerClient, Guid.Empty), "1010").GetProperty("debitAmount").GetInt64());

        // an amount of zero clears the balance
        var cleared = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.PutAsJsonAsync(OpeningBalancesUrl, Save(Guid.Empty, "2026-01-01", AccountLine(cashAccountId, 0, 0))));
        Assert.Equal(1, cleared.GetProperty("clearedCount").GetInt32());
        Assert.Equal(0, await NetBalanceOfAccountAsync(scenario.CompanyId, cashAccountId));
        Assert.Equal(0, await PartyTestHelpers.JournalDifferenceAsync(environment, scenario.CompanyId));
    }

    [Fact]
    public async Task A_credit_balance_and_the_difference_are_carried_by_opening_balance_equity()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var sheet = await GetSheetAsync(scenario.OwnerClient, Guid.Empty);
        var cashAccountId = FindRowByCode(sheet, "1010").GetProperty("accountId").GetGuid();
        var capitalAccountId = FindRowByCode(sheet, "3010").GetProperty("accountId").GetGuid();

        await TestScenarioBuilder.EnsureSuccessAsync(await scenario.OwnerClient.PutAsJsonAsync(OpeningBalancesUrl, Save(Guid.Empty, "2026-01-01",
            AccountLine(cashAccountId, 74_400, 0), AccountLine(capitalAccountId, 0, 60_000))));

        var after = await GetSheetAsync(scenario.OwnerClient, Guid.Empty);
        Assert.Equal(74_400, after.GetProperty("totalDebitAmount").GetInt64());
        Assert.Equal(60_000, after.GetProperty("totalCreditAmount").GetInt64());
        Assert.Equal(14_400, after.GetProperty("differenceAmount").GetInt64());

        var equityNet = await environment.Database.QueryScalarAsOwnerAsync<long>("""
            select coalesce(sum(l.debit - l.credit), 0)::bigint from acct.journal_lines l
              join acct.accounts a on a.company_id = l.company_id and a.id = l.account_id
             where l.company_id = $1 and a.system_role = 'OPENING_EQUITY'
            """, scenario.CompanyId);
        Assert.Equal(-14_400, equityNet);
        Assert.Equal(0, await PartyTestHelpers.JournalDifferenceAsync(environment, scenario.CompanyId));
    }

    [Fact]
    public async Task A_party_opening_balance_shows_in_the_party_balance_and_matches_the_ledger()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var partyId = await PartyTestHelpers.CreatePartyAsync(scenario.OwnerClient, "Ali Traders");

        await TestScenarioBuilder.EnsureSuccessAsync(await scenario.OwnerClient.PutAsJsonAsync(OpeningBalancesUrl, Save(Guid.Empty, "2026-01-01", PartyLine(partyId, 5_000, 0))));

        var party = await PartyTestHelpers.GetPartyAsync(scenario.OwnerClient, partyId);
        Assert.Equal(5_000, party.GetProperty("balanceAmount").GetInt64());
        Assert.Equal("DEBIT", party.GetProperty("openingBalance").GetProperty("sideCode").GetString());
        Assert.Equal(5_000, party.GetProperty("openingBalance").GetProperty("amount").GetInt64());

        var projection = await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select balance from party.party_balances where company_id = $1 and party_id = $2", scenario.CompanyId, partyId);
        var fromJournal = await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select coalesce(sum(debit - credit), 0)::bigint from acct.journal_lines where company_id = $1 and party_id = $2", scenario.CompanyId, partyId);
        Assert.Equal(fromJournal, projection);

        // changing it to a credit balance flips the party balance (reverse and repost)
        await TestScenarioBuilder.EnsureSuccessAsync(await scenario.OwnerClient.PutAsJsonAsync(OpeningBalancesUrl, Save(Guid.Empty, "2026-01-01", PartyLine(partyId, 0, 2_500))));
        Assert.Equal(-2_500, (await PartyTestHelpers.GetPartyAsync(scenario.OwnerClient, partyId)).GetProperty("balanceAmount").GetInt64());
    }

    [Fact]
    public async Task Invalid_lines_are_refused()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var sheet = await GetSheetAsync(scenario.OwnerClient, Guid.Empty);
        var cashAccountId = FindRowByCode(sheet, "1010").GetProperty("accountId").GetGuid();
        var stockAccountId = await environment.Database.QueryScalarAsOwnerAsync<Guid>(
            "select id from acct.accounts where company_id = $1 and system_role = 'STOCK' and store_id = '00000000-0000-0000-0000-000000000000'", scenario.CompanyId);

        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PutAsJsonAsync(OpeningBalancesUrl,
            Save(Guid.Empty, "2026-01-01", AccountLine(cashAccountId, 100, 100)))).StatusCode);                         // both sides
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PutAsJsonAsync(OpeningBalancesUrl,
            Save(Guid.Empty, "2026-01-01", AccountLine(cashAccountId, -5, 0)))).StatusCode);                            // negative
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PutAsJsonAsync(OpeningBalancesUrl,
            Save(Guid.Empty, "2026-01-01", AccountLine(stockAccountId, 100, 0)))).StatusCode);                          // stock account
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PutAsJsonAsync(OpeningBalancesUrl,
            Save(Guid.Empty, "2026-01-01", AccountLine(cashAccountId, 1, 0), AccountLine(cashAccountId, 2, 0)))).StatusCode);   // twice
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.OwnerClient.PutAsJsonAsync(OpeningBalancesUrl,
            Save(Guid.Empty, "2026-01-01"))).StatusCode);                                                               // nothing to save
        Assert.Equal(0, await PartyTestHelpers.JournalDifferenceAsync(environment, scenario.CompanyId));
    }

    [Fact]
    public async Task Saving_needs_the_opening_balance_restricted_permission_as_well_as_the_screen_permission()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var storeSheet = await GetSheetAsync(scenario.OwnerClient, scenario.FirstStoreId);
        var storeCashAccountId = FindRowByCode(storeSheet, "CASH-S1").GetProperty("accountId").GetGuid();
        var body = Save(scenario.FirstStoreId, "2026-01-01", AccountLine(storeCashAccountId, 9_000, 0));

        var screenOnly = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Settings.OpeningBalance.View, PermissionCodes.Settings.OpeningBalance.Add);
        var denied = await screenOnly.PutAsJsonAsync(OpeningBalancesUrl, body);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(ErrorCodes.PermissionDenied, (await OwnerCompanyScenario.ReadJsonAsync(denied)).GetProperty("errorCode").GetString());

        var viewerOnly = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Settings.OpeningBalance.View);
        Assert.Equal(HttpStatusCode.OK, (await viewerOnly.GetAsync(SheetUrl(scenario.FirstStoreId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewerOnly.PutAsJsonAsync(OpeningBalancesUrl, body)).StatusCode);

        var allowed = await scenario.SignInManagerWithPermissionsAsync(
            PermissionCodes.Settings.OpeningBalance.View, PermissionCodes.Settings.OpeningBalance.Add,
            PermissionCodes.RestrictedOperations.Accounting.OpeningBalance);
        await TestScenarioBuilder.EnsureSuccessAsync(await allowed.PutAsJsonAsync(OpeningBalancesUrl, body));
        Assert.Equal(9_000, await NetBalanceOfAccountAsync(scenario.CompanyId, storeCashAccountId));
    }

    [Fact]
    public async Task A_store_user_cannot_open_the_head_office_sheet_or_another_stores_sheet()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        var manager = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Settings.OpeningBalance.View);

        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync(SheetUrl(scenario.FirstStoreId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync(SheetUrl(Guid.Empty))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync(SheetUrl(secondStoreId))).StatusCode);
    }

    [Fact]
    public async Task One_company_cannot_post_opening_balances_on_the_accounts_of_another()
    {
        var companyA = await OwnerCompanyScenario.CreateAsync(environment);
        var companyB = await OwnerCompanyScenario.CreateAsync(environment);
        var cashAccountOfA = FindRowByCode(await GetSheetAsync(companyA.OwnerClient, Guid.Empty), "1010").GetProperty("accountId").GetGuid();

        var attempt = await companyB.OwnerClient.PutAsJsonAsync(OpeningBalancesUrl, Save(Guid.Empty, "2026-01-01", AccountLine(cashAccountOfA, 100, 0)));
        Assert.Equal(HttpStatusCode.BadRequest, attempt.StatusCode);
        Assert.Equal(0, await NetBalanceOfAccountAsync(companyA.CompanyId, cashAccountOfA));
    }

    [Fact]
    public async Task A_platform_administrator_is_refused_on_opening_balances()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.PlatformClient.GetAsync(SheetUrl(Guid.Empty))).StatusCode);
    }
}
