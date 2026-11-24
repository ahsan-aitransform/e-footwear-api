using System.Net;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Catalogue;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using static EFootwear.Tests.Inventory.InventoryTestHelpers;

namespace EFootwear.Tests.Inventory;

/// <summary>The stock adjustment, transfer and transfer summary reports (docx 8.20 to 8.22) and Stock Reconciliation: totals without join multiplication, the scope of each report, and a deliberate mismatch.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class InventoryReportsAndReconciliationApiTests(ApiTestEnvironment environment)
{
    private async Task<(OwnerCompanyScenario Scenario, Guid SecondStoreId, CatalogueLookups Lookups, InventoryItem Item)> NewShopAsync(decimal stock = 20)
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        var lookups = await LookupsAsync(scenario);
        return (scenario, secondStoreId, lookups, await CreateItemAsync(environment, scenario, lookups, "Gents Sandal", stock));
    }

    private static string AdjustmentReport => ReportUrl("reports/stock-adjustments");
    private static string TransferReport => ReportUrl("reports/stock-transfers");
    private static string TransferSummary => ReportUrl("reports/stock-transfer-summary");

    [Fact]
    public async Task The_adjustment_report_totals_the_adjustments_in_force_and_leaves_out_a_voided_one_and_its_reversal()
    {
        var (scenario, _, _, item) = await NewShopAsync();
        var owner = scenario.OwnerClient;
        await PostAdjustmentAsync(owner, scenario.FirstStoreId, item.VariantId, 5);
        await PostAdjustmentAsync(owner, scenario.FirstStoreId, item.VariantId, -2);
        var voided = await PostAdjustmentAsync(owner, scenario.FirstStoreId, item.VariantId, 7);
        await OwnerCompanyScenario.PostJsonAsync(owner, $"{AdjustmentUrl(voided.GetProperty("id").GetGuid())}/void", new { reason = "Typed twice", clientTransactionId = Guid.NewGuid() });

        var report = await GetJsonAsync(owner, $"{AdjustmentReport}?storeId={scenario.FirstStoreId}&fromDate={TestDate}&toDate={TestDate}");

        Assert.Equal(2, report.GetProperty("rows").GetArrayLength());
        Assert.Equal(5, report.GetProperty("totalAddedQuantity").GetDecimal());
        Assert.Equal(2, report.GetProperty("totalRemovedQuantity").GetDecimal());
        Assert.Equal(1_200, report.GetProperty("netValueAmount").GetInt64());
        Assert.Equal(item.Barcode, report.GetProperty("rows")[0].GetProperty("barcode").GetString());
    }

    [Fact]
    public async Task The_transfer_report_has_one_row_per_line_and_each_scope_returns_its_own_transfers()
    {
        var (scenario, secondStoreId, lookups, item) = await NewShopAsync();
        var owner = scenario.OwnerClient;
        var second = await CreateItemAsync(environment, scenario, lookups, "Kids Shoe", 10);
        var transferId = (await CreateTransferAsync(owner, scenario.FirstStoreId, secondStoreId)).GetProperty("id").GetGuid();
        await SendScanAsync(owner, transferId, item.Barcode, 4);
        await SendScanAsync(owner, transferId, second.Barcode, 2);
        await DispatchAsync(owner, transferId);
        await ReceiveScanAsync(owner, transferId, item.Barcode, 4);
        await ReceiveScanAsync(owner, transferId, second.Barcode, 1);
        await CompleteReceivingAsync(owner, transferId);
        var otherTransferId = await DispatchedTransferAsync(owner, scenario.FirstStoreId, secondStoreId, item, 1);

        var all = await GetJsonAsync(owner, $"{TransferReport}?scopeCode=ALL");
        Assert.Equal(3, all.GetProperty("rows").GetArrayLength());
        Assert.Equal(7, all.GetProperty("totalQuantityShipped").GetDecimal());
        Assert.Equal(5, all.GetProperty("totalQuantityReceived").GetDecimal());
        Assert.Equal(1, all.GetProperty("totalQuantityMissing").GetDecimal());
        Assert.Equal(2_800, all.GetProperty("totalValueAmount").GetInt64());

        var byTransfer = await GetJsonAsync(owner, $"{TransferReport}?scopeCode=BY_TRANSFER&transferNumber=1");
        Assert.Equal(2, byTransfer.GetProperty("rows").GetArrayLength());
        Assert.Equal(6, byTransfer.GetProperty("totalQuantityShipped").GetDecimal());
        var byStore = await GetJsonAsync(owner, $"{TransferReport}?scopeCode=BY_STORE&storeId={secondStoreId}");
        Assert.Equal(3, byStore.GetProperty("rows").GetArrayLength());

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"{TransferReport}?scopeCode=BY_TRANSFER")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"{TransferReport}?scopeCode=BY_STORE")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"{TransferReport}?scopeCode=NONSENSE")).StatusCode);
        Assert.NotEqual(Guid.Empty, otherTransferId);
    }

    [Fact]
    public async Task The_transfer_summary_adds_the_lines_of_each_store_pair_without_counting_a_transfer_twice()
    {
        var (scenario, secondStoreId, lookups, item) = await NewShopAsync();
        var owner = scenario.OwnerClient;
        var second = await CreateItemAsync(environment, scenario, lookups, "Kids Shoe", 10);
        var transferId = (await CreateTransferAsync(owner, scenario.FirstStoreId, secondStoreId)).GetProperty("id").GetGuid();
        await SendScanAsync(owner, transferId, item.Barcode, 4);
        await SendScanAsync(owner, transferId, second.Barcode, 2);
        await DispatchAsync(owner, transferId);
        await DispatchedTransferAsync(owner, scenario.FirstStoreId, secondStoreId, item, 1);
        await CreateTransferAsync(owner, scenario.FirstStoreId, secondStoreId);

        var summary = await GetJsonAsync(owner, $"{TransferSummary}?storeId={scenario.FirstStoreId}");

        Assert.Equal(1, summary.GetProperty("rows").GetArrayLength());
        var row = summary.GetProperty("rows")[0];
        Assert.Equal(2, row.GetProperty("transferCount").GetInt32());
        Assert.Equal(7, row.GetProperty("quantityShipped").GetDecimal());
        Assert.Equal(2_800, row.GetProperty("valueAmount").GetInt64());
        Assert.Equal(2, summary.GetProperty("totalTransferCount").GetInt32());
    }

    [Fact]
    public async Task Reconciliation_is_clean_after_real_movements_and_names_a_size_whose_balance_was_changed_behind_the_ledger()
    {
        var (scenario, secondStoreId, _, item) = await NewShopAsync();
        var owner = scenario.OwnerClient;
        await PostAdjustmentAsync(owner, scenario.FirstStoreId, item.VariantId, -3);
        var transferId = await DispatchedTransferAsync(owner, scenario.FirstStoreId, secondStoreId, item, 5);
        await ReceiveScanAsync(owner, transferId, item.Barcode, 4);
        await CompleteReceivingAsync(owner, transferId);
        await DispatchedTransferAsync(owner, scenario.FirstStoreId, secondStoreId, item, 2);

        var clean = await GetJsonAsync(owner, ReconciliationUrl);
        Assert.True(clean.GetProperty("isClean").GetBoolean());
        Assert.Equal(0, clean.GetProperty("mismatches").GetArrayLength());
        Assert.Equal(1, clean.GetProperty("transfersInTransitCount").GetInt32());
        Assert.Equal(2, clean.GetProperty("piecesInTransitQuantity").GetDecimal());
        Assert.Equal(1, clean.GetProperty("openLossCaseCount").GetInt32());
        Assert.Equal(2, clean.GetProperty("checkedSizeCount").GetInt32());

        await environment.Database.ExecuteAsOwnerAsync(
            "update inv.stock_balances set qty_on_hand = qty_on_hand + 2 where company_id = $1 and store_id = $2 and variant_id = $3", scenario.CompanyId, scenario.FirstStoreId, item.VariantId);

        var dirty = await GetJsonAsync(owner, $"{ReconciliationUrl}?storeId={scenario.FirstStoreId}");
        Assert.False(dirty.GetProperty("isClean").GetBoolean());
        var mismatch = dirty.GetProperty("mismatches")[0];
        Assert.Equal(item.Barcode, mismatch.GetProperty("barcode").GetString());
        Assert.Equal(2, mismatch.GetProperty("differenceQuantity").GetDecimal());
        var otherStore = await GetJsonAsync(owner, $"{ReconciliationUrl}?storeId={secondStoreId}");
        Assert.True(otherStore.GetProperty("isClean").GetBoolean());
    }

    [Fact]
    public async Task The_reports_and_the_reconciliation_need_their_own_permission_and_a_store_the_user_works_in()
    {
        var (scenario, secondStoreId, _, _) = await NewShopAsync();
        var noPermission = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Invoice.StockAdjustment.View);
        var reportReader = await scenario.SignInManagerWithPermissionsAsync(
            PermissionCodes.Reports.StockAdjustmentReport.View, PermissionCodes.Reports.StockTransferReport.View, PermissionCodes.Reports.StockTransferSummary.View,
            PermissionCodes.Inventory.StockReconciliation.View);

        foreach (var url in new[] { AdjustmentReport, TransferReport + "?scopeCode=ALL", TransferSummary, ReconciliationUrl })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await noPermission.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await reportReader.GetAsync(url)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await reportReader.GetAsync($"{AdjustmentReport}?storeId={secondStoreId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reportReader.GetAsync($"{ReconciliationUrl}?storeId={secondStoreId}")).StatusCode);

        var otherCompany = await OwnerCompanyScenario.CreateAsync(environment);
        var empty = await GetJsonAsync(otherCompany.OwnerClient, AdjustmentReport);
        Assert.Equal(0, empty.GetProperty("rows").GetArrayLength());
    }
}
