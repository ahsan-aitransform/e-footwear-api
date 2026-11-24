using System.Net;
using System.Net.Http.Json;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Catalogue;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using static EFootwear.Tests.Accounting.VoucherTestHelpers;
using static EFootwear.Tests.Inventory.InventoryTestHelpers;

namespace EFootwear.Tests.Inventory;

/// <summary>Stock Audit (docx 5.12 and 8.24): counting by scanning never touches stock, the comparison with the books, "Adjust Stock", the safe retry of a scan and the isolation of stores.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class StockAuditApiTests(ApiTestEnvironment environment)
{
    private async Task<(OwnerCompanyScenario Scenario, CatalogueLookups Lookups, InventoryItem Item)> NewShopAsync(decimal stock = 10)
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var lookups = await LookupsAsync(scenario);
        return (scenario, lookups, await CreateItemAsync(environment, scenario, lookups, "Gents Sandal", stock));
    }

    [Fact]
    public async Task Scanning_counts_the_pieces_without_moving_stock_and_a_repeated_scan_id_counts_nothing_twice()
    {
        var (scenario, _, item) = await NewShopAsync();
        var audit = await CreateAuditAsync(scenario.OwnerClient, scenario.FirstStoreId);
        var auditId = audit.GetProperty("id").GetGuid();
        Assert.Equal("DRAFT", audit.GetProperty("statusCode").GetString());

        var scanId = Guid.NewGuid();
        var first = await ScanAuditAsync(scenario.OwnerClient, auditId, item.Barcode, 3, scanId);
        var repeated = await ScanAuditAsync(scenario.OwnerClient, auditId, item.Barcode, 3, scanId);
        var another = await ScanAuditAsync(scenario.OwnerClient, auditId, item.Barcode, 1);

        Assert.Equal(3, first.GetProperty("countedQuantity").GetDecimal());
        Assert.False(first.GetProperty("wasAlreadyRecorded").GetBoolean());
        Assert.True(repeated.GetProperty("wasAlreadyRecorded").GetBoolean());
        Assert.Equal(3, repeated.GetProperty("countedQuantity").GetDecimal());
        Assert.Equal(4, another.GetProperty("countedQuantity").GetDecimal());
        Assert.Equal(10, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));

        var comparison = await GetJsonAsync(scenario.OwnerClient, AuditUrl(auditId));
        var line = comparison.GetProperty("lines")[0];
        Assert.Equal(10, line.GetProperty("expectedQuantity").GetDecimal());
        Assert.Equal(4, line.GetProperty("countedQuantity").GetDecimal());
        Assert.Equal(-6, line.GetProperty("differenceQuantity").GetDecimal());
        Assert.Equal(-2_400, line.GetProperty("differenceAmount").GetInt64());
        Assert.Equal(1, comparison.GetProperty("summary").GetProperty("lessItemCount").GetInt32());
    }

    [Fact]
    public async Task A_correction_cannot_take_the_count_below_zero_and_an_unknown_barcode_is_refused()
    {
        var (scenario, _, item) = await NewShopAsync();
        var auditId = (await CreateAuditAsync(scenario.OwnerClient, scenario.FirstStoreId)).GetProperty("id").GetGuid();
        await ScanAuditAsync(scenario.OwnerClient, auditId, item.Barcode, 2);

        var corrected = await ScanAuditAsync(scenario.OwnerClient, auditId, item.Barcode, -1);
        Assert.Equal(1, corrected.GetProperty("countedQuantity").GetDecimal());
        var below = await scenario.OwnerClient.PostAsJsonAsync($"{AuditUrl(auditId)}/scans", new { barcode = item.Barcode, quantity = -5, deviceScanId = Guid.NewGuid() });
        Assert.False(below.IsSuccessStatusCode);
        var unknown = await scenario.OwnerClient.PostAsJsonAsync($"{AuditUrl(auditId)}/scans", new { barcode = "000000000", quantity = 1, deviceScanId = Guid.NewGuid() });
        Assert.False(unknown.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Adjust_stock_posts_the_differences_once_balances_the_journal_and_closes_the_audit()
    {
        var (scenario, _, item) = await NewShopAsync(stock: 10);
        var auditId = (await CreateAuditAsync(scenario.OwnerClient, scenario.FirstStoreId)).GetProperty("id").GetGuid();
        await ScanAuditAsync(scenario.OwnerClient, auditId, item.Barcode, 7);

        var posted = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, $"{AuditUrl(auditId)}/post", new { includeUnscannedItems = false });
        var postedAgain = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, $"{AuditUrl(auditId)}/post", new { includeUnscannedItems = false });

        Assert.Equal("POSTED", posted.GetProperty("statusCode").GetString());
        Assert.Equal("POSTED", postedAgain.GetProperty("statusCode").GetString());
        Assert.Equal(7, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));
        Assert.Equal(1, await JournalEntryCountAsync(environment, scenario.CompanyId, "STOCK_AUDIT"));
        var line = posted.GetProperty("lines")[0];
        Assert.Equal(10, line.GetProperty("expectedQuantity").GetDecimal());
        Assert.Equal(7, line.GetProperty("countedQuantity").GetDecimal());

        var late = await scenario.OwnerClient.PostAsJsonAsync($"{AuditUrl(auditId)}/scans", new { barcode = item.Barcode, quantity = 1, deviceScanId = Guid.NewGuid() });
        Assert.False(late.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.OwnerClient.DeleteAsync(AuditUrl(auditId))).StatusCode);
    }

    [Fact]
    public async Task Counted_pieces_that_match_the_books_post_nothing_and_unscanned_sizes_are_booked_at_zero_only_when_asked()
    {
        var (scenario, lookups, item) = await NewShopAsync(stock: 10);
        var unscanned = await CreateItemAsync(environment, scenario, lookups, "Kids Shoe", 5);

        var matching = (await CreateAuditAsync(scenario.OwnerClient, scenario.FirstStoreId)).GetProperty("id").GetGuid();
        await ScanAuditAsync(scenario.OwnerClient, matching, item.Barcode, 10);
        var posted = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, $"{AuditUrl(matching)}/post", new { includeUnscannedItems = false });
        Assert.Equal(1, posted.GetProperty("summary").GetProperty("matchedItemCount").GetInt32());
        Assert.Equal(10, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
        Assert.Equal(5, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, unscanned.VariantId));

        var withUnscanned = (await CreateAuditAsync(scenario.OwnerClient, scenario.FirstStoreId)).GetProperty("id").GetGuid();
        await ScanAuditAsync(scenario.OwnerClient, withUnscanned, item.Barcode, 10);
        var preview = await GetJsonAsync(scenario.OwnerClient, $"{AuditUrl(withUnscanned)}?includeUnscannedItems=true");
        Assert.Equal(2, preview.GetProperty("lines").GetArrayLength());
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, $"{AuditUrl(withUnscanned)}/post", new { includeUnscannedItems = true });
        Assert.Equal(0, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, unscanned.VariantId));
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));
    }

    [Fact]
    public async Task An_audit_can_be_deleted_only_while_it_is_being_counted()
    {
        var (scenario, _, item) = await NewShopAsync();
        var auditId = (await CreateAuditAsync(scenario.OwnerClient, scenario.FirstStoreId)).GetProperty("id").GetGuid();
        await ScanAuditAsync(scenario.OwnerClient, auditId, item.Barcode, 1);

        Assert.Equal(HttpStatusCode.NoContent, (await scenario.OwnerClient.DeleteAsync(AuditUrl(auditId))).StatusCode);

        Assert.Equal("CANCELLED", (await GetJsonAsync(scenario.OwnerClient, AuditUrl(auditId))).GetProperty("statusCode").GetString());
        Assert.Equal(10, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
    }

    [Fact]
    public async Task Starting_an_audit_twice_with_the_same_transaction_id_gives_the_same_audit()
    {
        var (scenario, _, _) = await NewShopAsync();
        var transactionId = Guid.NewGuid();

        var first = await CreateAuditAsync(scenario.OwnerClient, scenario.FirstStoreId, transactionId);
        var second = await CreateAuditAsync(scenario.OwnerClient, scenario.FirstStoreId, transactionId);

        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal(1, await DocumentCountAsync(environment, scenario.CompanyId, "AUDIT"));
    }

    [Fact]
    public async Task The_permission_matrix_and_the_store_and_company_isolation_of_audits()
    {
        var (scenario, _, item) = await NewShopAsync();
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        var auditId = (await CreateAuditAsync(scenario.OwnerClient, scenario.FirstStoreId)).GetProperty("id").GetGuid();

        var viewOnly = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Invoice.StockAudit.View);
        Assert.Equal(HttpStatusCode.OK, (await viewOnly.GetAsync(AuditUrl(auditId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewOnly.PostAsJsonAsync(AuditsUrl, new { storeId = scenario.FirstStoreId, auditDate = TestDate, clientTransactionId = Guid.NewGuid(), note = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewOnly.PostAsJsonAsync($"{AuditUrl(auditId)}/post", new { includeUnscannedItems = false })).StatusCode);

        var counter = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Invoice.StockAudit.View, PermissionCodes.Invoice.StockAudit.Edit, PermissionCodes.RestrictedOperations.Stock.StockAudit);
        Assert.Equal(HttpStatusCode.OK, (await counter.PostAsJsonAsync($"{AuditUrl(auditId)}/scans", new { barcode = item.Barcode, quantity = 1, deviceScanId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await counter.PostAsJsonAsync($"{AuditUrl(auditId)}/post", new { includeUnscannedItems = false })).StatusCode);

        var otherStoreUser = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [secondStoreId],
            PermissionCodes.Invoice.StockAudit.View, PermissionCodes.Invoice.StockAudit.Edit, PermissionCodes.RestrictedOperations.Stock.StockAudit);
        Assert.True((await otherStoreUser.GetAsync(AuditUrl(auditId))).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        Assert.False((await otherStoreUser.PostAsJsonAsync($"{AuditUrl(auditId)}/scans", new { barcode = item.Barcode, quantity = 1, deviceScanId = Guid.NewGuid() })).IsSuccessStatusCode);

        var otherCompany = await OwnerCompanyScenario.CreateAsync(environment);
        Assert.Equal(HttpStatusCode.NotFound, (await otherCompany.OwnerClient.GetAsync(AuditUrl(auditId))).StatusCode);
        Assert.False((await otherCompany.OwnerClient.PostAsJsonAsync($"{AuditUrl(auditId)}/scans", new { barcode = item.Barcode, quantity = 1, deviceScanId = Guid.NewGuid() })).IsSuccessStatusCode);
        Assert.Equal(10, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
    }
}
