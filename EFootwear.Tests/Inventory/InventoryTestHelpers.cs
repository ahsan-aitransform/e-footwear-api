using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Catalogue;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;

namespace EFootwear.Tests.Inventory;

/// <summary>Shared helpers of the Inventory tests: urls, stocked items, request bodies and the database reads that check stock, the ledger and the journal.</summary>
public static class InventoryTestHelpers
{
    public const string TestDate = "2026-11-10";

    public static readonly string InventoryUrl = TestScenarioBuilder.Url(ApiRoutes.Inventory.Root);
    public static string AdjustmentsUrl => $"{InventoryUrl}/{ApiRoutes.Inventory.StockAdjustments}";
    public static string AdjustmentUrl(Guid id) => $"{AdjustmentsUrl}/{id}";
    public static string NonZeroStockUrl => $"{InventoryUrl}/{ApiRoutes.Inventory.NonZeroStock}";
    public static string CountUpdatesUrl => $"{InventoryUrl}/{ApiRoutes.Inventory.NonZeroStockCountUpdates}";
    public static string CountFilePreviewUrl => $"{InventoryUrl}/{ApiRoutes.Inventory.NonZeroStockCountFilePreview}";
    public static string AuditsUrl => $"{InventoryUrl}/{ApiRoutes.Inventory.StockAudits}";
    public static string AuditUrl(Guid id) => $"{AuditsUrl}/{id}";
    public static string ClaimsUrl => $"{InventoryUrl}/{ApiRoutes.Inventory.ProductClaims}";
    public static string ClaimApprovalsUrl => $"{InventoryUrl}/{ApiRoutes.Inventory.ClaimApprovals}";
    public static string TransfersUrl => $"{InventoryUrl}/{ApiRoutes.Inventory.StockTransfers}";
    public static string TransferUrl(Guid id) => $"{TransfersUrl}/{id}";
    public static string ReceivingUrl => $"{InventoryUrl}/{ApiRoutes.Inventory.StockTransferReceiving}";
    public static string DiscrepanciesUrl => $"{InventoryUrl}/{ApiRoutes.Inventory.TransferDiscrepancies}";
    public static string ReconciliationUrl => $"{InventoryUrl}/{ApiRoutes.Inventory.StockReconciliation}";
    public static string ReportUrl(string tail) => $"{InventoryUrl}/{tail}";

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Task<CatalogueLookups>> LookupsByCompany = new();

    /// <summary>The classification lookups of the company of a test scenario, created once (a second creation would collide on the names).</summary>
    public static Task<CatalogueLookups> LookupsAsync(OwnerCompanyScenario scenario) =>
        LookupsByCompany.GetOrAdd(scenario.CompanyId, _ => CatalogueTestHelpers.CreateLookupsAsync(scenario.OwnerClient));

    public sealed record InventoryItem(Guid VariantId, string Barcode, string ProductName);

    /// <summary>A product with one size (purchase price 400, sale price 800) and stock in the store, with the opening row in the stock ledger.</summary>
    public static async Task<InventoryItem> CreateItemAsync(
        ApiTestEnvironment environment, OwnerCompanyScenario scenario, CatalogueLookups lookups, string name, decimal stock, Guid? storeId = null)
    {
        var created = await CatalogueTestHelpers.CreateProductAsync(scenario.OwnerClient, lookups, name, CatalogueTestHelpers.Variant("42", 400, 800));
        var productId = created.GetProperty("productId").GetGuid();
        var variant = CatalogueTestHelpers.ReadVariants(await CatalogueTestHelpers.GetProductAsync(scenario.OwnerClient, productId)).Single();
        if (stock > 0) await SeedStockAsync(environment, scenario.CompanyId, storeId ?? scenario.FirstStoreId, variant.VariantId, stock);
        return new InventoryItem(variant.VariantId, variant.Barcode, name);
    }

    /// <summary>Opening stock: the balance and its ledger row together, so the reconciliation of a test company starts clean.</summary>
    public static async Task SeedStockAsync(ApiTestEnvironment environment, Guid companyId, Guid storeId, Guid variantId, decimal quantity)
    {
        await CatalogueTestHelpers.SeedStockAsync(environment, companyId, storeId, variantId, quantity);
        await environment.Database.ExecuteAsOwnerAsync("update inv.stock_balances set avg_cost = 400 where company_id = $1 and store_id = $2 and variant_id = $3", companyId, storeId, variantId);
        await CatalogueTestHelpers.SeedLedgerRowAsync(environment, companyId, storeId, variantId, new DateOnly(2026, 11, 1), "OPENING", quantity, quantity);
    }

    public static Task<decimal> StockAsync(ApiTestEnvironment environment, Guid companyId, Guid storeId, Guid variantId) =>
        Sales.SalesTestHelpers.StockOnHandAsync(environment, companyId, storeId, variantId);

    public static async Task<long> JournalEntryCountAsync(ApiTestEnvironment environment, Guid companyId, string sourceType) =>
        await environment.Database.QueryScalarAsOwnerAsync<long>("select count(*) from acct.journal_entries where company_id = $1 and source_type = $2", companyId, sourceType);

    public static async Task<long> DocumentCountAsync(ApiTestEnvironment environment, Guid companyId, string documentType) =>
        await environment.Database.QueryScalarAsOwnerAsync<long>("select count(*) from inv.documents where company_id = $1 and doc_type = $2", companyId, documentType);

    public static object AdjustmentLine(Guid variantId, decimal quantity, long? unitCost = 400, string? reason = null) =>
        new { variantId, quantity, unitCostAmount = unitCost, reasonText = reason };

    public static object AdjustmentBody(Guid storeId, IReadOnlyList<object> lines, Guid? clientTransactionId = null, string? description = null, string? date = null) =>
        new { storeId, adjustmentDate = date ?? TestDate, clientTransactionId = clientTransactionId ?? Guid.NewGuid(), description, lines };

    public static Task<JsonElement> PostAdjustmentAsync(HttpClient client, Guid storeId, Guid variantId, decimal quantity) =>
        OwnerCompanyScenario.PostJsonAsync(client, AdjustmentsUrl, AdjustmentBody(storeId, [AdjustmentLine(variantId, quantity)]));

    public static async Task<JsonElement> CreateAuditAsync(HttpClient client, Guid storeId, Guid? clientTransactionId = null) =>
        await OwnerCompanyScenario.PostJsonAsync(client, AuditsUrl, new { storeId, auditDate = TestDate, clientTransactionId = clientTransactionId ?? Guid.NewGuid(), note = (string?)null });

    public static async Task<JsonElement> ScanAuditAsync(HttpClient client, Guid auditId, string barcode, int quantity = 1, Guid? deviceScanId = null) =>
        await OwnerCompanyScenario.PostJsonAsync(client, $"{AuditUrl(auditId)}/scans", new { barcode, quantity, deviceScanId = deviceScanId ?? Guid.NewGuid() });

    public static async Task<JsonElement> CreateClaimAsync(HttpClient client, Guid storeId, Guid variantId, decimal quantity, Guid? clientTransactionId = null) =>
        await OwnerCompanyScenario.PostJsonAsync(client, ClaimsUrl, new
        {
            storeId, claimDate = TestDate, clientTransactionId = clientTransactionId ?? Guid.NewGuid(), variantId, quantity, reasonCode = "DAMAGED", note = "Sole came off",
        });

    public static async Task<JsonElement> CreateTransferAsync(HttpClient client, Guid fromStoreId, Guid toStoreId, Guid? clientTransactionId = null) =>
        await OwnerCompanyScenario.PostJsonAsync(client, TransfersUrl, new { fromStoreId, toStoreId, clientTransactionId = clientTransactionId ?? Guid.NewGuid(), note = "Weekly top-up" });

    public static async Task<JsonElement> SendScanAsync(HttpClient client, Guid transferId, string barcode, int quantity, Guid? scanBatchId = null) =>
        await OwnerCompanyScenario.PostJsonAsync(client, $"{TransferUrl(transferId)}/send-scans", new { barcode, quantity, scanBatchId = scanBatchId ?? Guid.NewGuid() });

    public static async Task<JsonElement> ReceiveScanAsync(HttpClient client, Guid transferId, string barcode, int quantity, Guid? scanBatchId = null) =>
        await OwnerCompanyScenario.PostJsonAsync(client, $"{ReceivingUrl}/{transferId}/scans", new { barcode, quantity, scanBatchId = scanBatchId ?? Guid.NewGuid() });

    public static async Task<JsonElement> DispatchAsync(HttpClient client, Guid transferId) =>
        await OwnerCompanyScenario.PostJsonAsync(client, $"{TransferUrl(transferId)}/dispatch", new { });

    public static async Task<JsonElement> CompleteReceivingAsync(HttpClient client, Guid transferId) =>
        await OwnerCompanyScenario.PostJsonAsync(client, $"{ReceivingUrl}/{transferId}/complete", new { });

    /// <summary>A transfer carried from draft to dispatched with the pieces scanned in the sender; returns the transfer id.</summary>
    public static async Task<Guid> DispatchedTransferAsync(HttpClient client, Guid fromStoreId, Guid toStoreId, InventoryItem item, int quantity)
    {
        var transferId = (await CreateTransferAsync(client, fromStoreId, toStoreId)).GetProperty("id").GetGuid();
        await SendScanAsync(client, transferId, item.Barcode, quantity);
        await DispatchAsync(client, transferId);
        return transferId;
    }

    public static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        await TestScenarioBuilder.EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task<string> ProblemMessageAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString() ?? "";

    public static string[] ManagerPermissions(params string[] permissions) => permissions;
}
