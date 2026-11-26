using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Tests.Catalogue;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;

namespace EFootwear.Tests.Sales;

/// <summary>Shared helpers of the Sales tests: urls, a stocked product, request bodies, and the database reads that check stock and the journal.</summary>
public static class SalesTestHelpers
{
    public const string TestSaleDate = "2026-11-10";

    public static readonly string SalesUrl = TestScenarioBuilder.Url(ApiRoutes.Sales.Root);
    public static string NewSaleUrl => $"{SalesUrl}/{ApiRoutes.Sales.NewSale}";
    public static string SaleReturnsUrl => $"{SalesUrl}/{ApiRoutes.Sales.SaleReturns}";
    public static string ExchangesUrl => $"{SalesUrl}/{ApiRoutes.Sales.Exchanges}";
    public static string InvoicesUrl => $"{SalesUrl}/{ApiRoutes.Sales.Invoices}";
    public static string DeletedInvoicesUrl => $"{SalesUrl}/{ApiRoutes.Sales.DeletedInvoices}";

    public static string InvoiceUrl(Guid invoiceId) => $"{InvoicesUrl}/{invoiceId}";
    public static string VoidUrl(Guid invoiceId) => $"{InvoiceUrl(invoiceId)}/void";
    public static string VoidAndReenterUrl(Guid invoiceId) => $"{InvoiceUrl(invoiceId)}/void-and-reenter";

    public sealed record StockedItem(Guid ProductId, Guid VariantId, string Barcode, long ShelfPrice);

    /// <summary>A product with one size, a shelf price and stock in the first store.</summary>
    public static async Task<StockedItem> CreateStockedItemAsync(
        ApiTestEnvironment environment, OwnerCompanyScenario scenario, CatalogueLookups lookups, string name, long shelfPrice, decimal stock)
    {
        var created = await CatalogueTestHelpers.CreateProductAsync(
            scenario.OwnerClient, lookups, name, CatalogueTestHelpers.Variant("42", Math.Max(1, shelfPrice / 2), shelfPrice));
        var productId = created.GetProperty("productId").GetGuid();
        var variant = CatalogueTestHelpers.ReadVariants(await CatalogueTestHelpers.GetProductAsync(scenario.OwnerClient, productId)).Single();
        await CatalogueTestHelpers.SeedStockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, variant.VariantId, stock);
        return new StockedItem(productId, variant.VariantId, variant.Barcode, shelfPrice);
    }

    public static Task<Guid> CashAccountIdAsync(ApiTestEnvironment environment, Guid companyId) =>
        Accounting.VoucherTestHelpers.FirstStoreCashAccountIdAsync(environment, companyId);

    public static object Line(Guid variantId, decimal quantity, long unitPrice, string? discountType = null, decimal? discountValue = null) =>
        new { variantId, quantity, unitPrice, discountTypeCode = discountType, discountValue };

    public static object CashPayment(Guid accountId, long amount) =>
        new { paymentMethodCode = "CASH", paymentAccountId = accountId, amount, referenceNumber = (string?)null };

    public static object SaleBody(
        Guid storeId, IReadOnlyList<object> lines, IReadOnlyList<object> payments, long cashReturned = 0, Guid? clientTransactionId = null,
        Guid? customerId = null, string? description = null, string? walkInName = null, string? walkInMobile = null) =>
        new
        {
            storeId,
            saleDate = TestSaleDate,
            clientTransactionId = clientTransactionId ?? Guid.NewGuid(),
            customerId,
            salesmanStaffId = (Guid?)null,
            walkInCustomerName = walkInName,
            walkInCustomerMobile = walkInMobile,
            posTerminalDeviceId = (Guid?)null,
            description,
            lines,
            payments,
            cashReturnedAmount = cashReturned,
        };

    public static object ReturnedLine(Guid originalLineId, decimal quantity, string? reason = "Does not fit", string? condition = "SELLABLE") =>
        new { originalLineId, quantity, reasonText = reason, itemConditionCode = condition };

    public static object ReturnBody(Guid storeId, IReadOnlyList<object> lines, Guid? clientTransactionId = null, Guid? customerId = null) =>
        new { storeId, returnDate = TestSaleDate, clientTransactionId = clientTransactionId ?? Guid.NewGuid(), customerId, description = (string?)null, lines };

    public static object ExchangeBody(
        Guid storeId, IReadOnlyList<object> returnedLines, IReadOnlyList<object> newSaleLines, IReadOnlyList<object> payments, long cashReturned = 0,
        Guid? customerId = null) =>
        new
        {
            storeId,
            exchangeDate = TestSaleDate,
            clientTransactionId = Guid.NewGuid(),
            customerId,
            salesmanStaffId = (Guid?)null,
            walkInCustomerName = (string?)null,
            walkInCustomerMobile = (string?)null,
            posTerminalDeviceId = (Guid?)null,
            description = (string?)null,
            returnedLines,
            newSaleLines,
            payments,
            cashReturnedAmount = cashReturned,
        };

    public static async Task<JsonElement> PostSaleAsync(HttpClient client, object body) => await OwnerCompanyScenario.PostJsonAsync(client, NewSaleUrl, body);

    /// <summary>One cash sale of one line, paid exactly. Returns the stored invoice.</summary>
    public static async Task<JsonElement> PostSimpleCashSaleAsync(
        ApiTestEnvironment environment, OwnerCompanyScenario scenario, StockedItem item, decimal quantity)
    {
        var cashAccountId = await CashAccountIdAsync(environment, scenario.CompanyId);
        return await PostSaleAsync(scenario.OwnerClient, SaleBody(
            scenario.FirstStoreId, [Line(item.VariantId, quantity, item.ShelfPrice)], [CashPayment(cashAccountId, item.ShelfPrice * (long)quantity)]));
    }

    public static Guid FirstLineId(JsonElement invoice) => invoice.GetProperty("lines")[0].GetProperty("lineId").GetGuid();

    public static async Task<decimal> StockOnHandAsync(ApiTestEnvironment environment, Guid companyId, Guid storeId, Guid variantId) =>
        await environment.Database.QueryScalarAsOwnerAsync<decimal>(
            "select coalesce(sum(qty_on_hand), 0) from inv.stock_balances where company_id = $1 and store_id = $2 and variant_id = $3", companyId, storeId, variantId);

    public static async Task<long> InvoiceCountAsync(ApiTestEnvironment environment, Guid companyId) =>
        await environment.Database.QueryScalarAsOwnerAsync<long>("select count(*) from sales.invoices where company_id = $1", companyId);

    public static async Task<string> ProblemMessageAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString() ?? "";
}
