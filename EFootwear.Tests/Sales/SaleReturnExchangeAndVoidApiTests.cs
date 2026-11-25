using System.Net;
using System.Net.Http.Json;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Catalogue;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using EFootwear.Tests.Contacts;
using static EFootwear.Tests.Accounting.VoucherTestHelpers;
using static EFootwear.Tests.Sales.SalesTestHelpers;

namespace EFootwear.Tests.Sales;

/// <summary>
/// Sale Return, Exchange Invoice, voiding and "void and re-enter" (docx 5.2, 5.3, 5.13; Parts 11 and 12). Every return names the sale line it brings back,
/// the returnable quantity is checked for the whole request, an exchange is one transaction, and a void puts everything back.
/// </summary>
[Collection(ApiIntegrationCollection.Name)]
public class SaleReturnExchangeAndVoidApiTests(ApiTestEnvironment environment)
{
    private async Task<(OwnerCompanyScenario Scenario, StockedItem Item, CatalogueLookups Lookups)> NewShopAsync(long shelfPrice = 1_000, decimal stock = 10)
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var lookups = await CatalogueTestHelpers.CreateLookupsAsync(scenario.OwnerClient);
        return (scenario, await CreateStockedItemAsync(environment, scenario, lookups, "Ladies Sandal", shelfPrice, stock), lookups);
    }

    // ------------------------------------------------------------ returns

    [Fact]
    public async Task A_partial_return_is_linked_to_the_original_puts_the_stock_back_and_leaves_the_rest_returnable()
    {
        var (scenario, item, _) = await NewShopAsync();
        var sale = await PostSimpleCashSaleAsync(environment, scenario, item, 3);
        var saleLineId = FirstLineId(sale);

        var found = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(
            $"{SalesUrl}/{E_Footwear_Api.Routing.ApiRoutes.Sales.SaleReturnInvoiceByNumber}?storeId={scenario.FirstStoreId}&invoiceNumber=1"));
        Assert.Equal(3, found.GetProperty("lines")[0].GetProperty("returnableQuantity").GetDecimal());

        var returned = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, SaleReturnsUrl,
            ReturnBody(scenario.FirstStoreId, [ReturnedLine(saleLineId, 1, "Too tight", "SELLABLE")]));

        Assert.Equal("RETURN", returned.GetProperty("documentTypeCode").GetString());
        Assert.Equal(-1_000, returned.GetProperty("netAmount").GetInt64());
        Assert.Equal(sale.GetProperty("id").GetGuid(), returned.GetProperty("originalInvoiceId").GetGuid());
        Assert.Equal("Too tight", returned.GetProperty("lines")[0].GetProperty("returnReason").GetString());
        Assert.Equal(8, await StockOnHandAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));

        var after = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(
            $"{SalesUrl}/{E_Footwear_Api.Routing.ApiRoutes.Sales.SaleReturnInvoiceByNumber}?storeId={scenario.FirstStoreId}&invoiceNumber=1"));
        var line = after.GetProperty("lines")[0];
        Assert.Equal(1, line.GetProperty("alreadyReturnedQuantity").GetDecimal());
        Assert.Equal(2, line.GetProperty("returnableQuantity").GetDecimal());
    }

    [Fact]
    public async Task Returning_more_than_was_sold_returning_twice_or_naming_the_same_line_twice_is_refused()
    {
        var (scenario, item, _) = await NewShopAsync();
        var saleLineId = FirstLineId(await PostSimpleCashSaleAsync(environment, scenario, item, 2));

        var tooMany = await scenario.OwnerClient.PostAsJsonAsync(SaleReturnsUrl, ReturnBody(scenario.FirstStoreId, [ReturnedLine(saleLineId, 3)]));
        Assert.False(tooMany.IsSuccessStatusCode);

        var sameLineTwice = await scenario.OwnerClient.PostAsJsonAsync(SaleReturnsUrl,
            ReturnBody(scenario.FirstStoreId, [ReturnedLine(saleLineId, 1.5m), ReturnedLine(saleLineId, 1.5m)]));
        Assert.False(sameLineTwice.IsSuccessStatusCode);
        Assert.Equal(8, await StockOnHandAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));

        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, SaleReturnsUrl, ReturnBody(scenario.FirstStoreId, [ReturnedLine(saleLineId, 2)]));
        var afterFullReturn = await scenario.OwnerClient.PostAsJsonAsync(SaleReturnsUrl, ReturnBody(scenario.FirstStoreId, [ReturnedLine(saleLineId, 1)]));
        Assert.False(afterFullReturn.IsSuccessStatusCode);
    }

    [Fact]
    public async Task A_return_sent_twice_with_the_same_client_transaction_is_posted_once()
    {
        var (scenario, item, _) = await NewShopAsync();
        var saleLineId = FirstLineId(await PostSimpleCashSaleAsync(environment, scenario, item, 2));
        var body = ReturnBody(scenario.FirstStoreId, [ReturnedLine(saleLineId, 1)], clientTransactionId: Guid.NewGuid());

        var first = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, SaleReturnsUrl, body);
        var second = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, SaleReturnsUrl, body);

        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal(9, await StockOnHandAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
    }

    [Fact]
    public async Task A_return_of_a_credit_sale_to_a_customer_lowers_the_balance_of_the_customer()
    {
        var (scenario, item, _) = await NewShopAsync();
        var customerId = await ContactTestHelpers.CreateCustomerAsync(scenario.OwnerClient, "Chaudhry Bilal");
        var sale = await PostSaleAsync(scenario.OwnerClient, SaleBody(scenario.FirstStoreId, [Line(item.VariantId, 2, 1_000)], [], customerId: customerId));
        Assert.Equal(2_000, await CustomerNetBalanceAsync(environment, scenario.CompanyId, customerId));

        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, SaleReturnsUrl,
            ReturnBody(scenario.FirstStoreId, [ReturnedLine(FirstLineId(sale), 1)], customerId: customerId));

        Assert.Equal(1_000, await CustomerNetBalanceAsync(environment, scenario.CompanyId, customerId));
    }

    [Fact]
    public async Task A_return_needs_the_return_permissions()
    {
        var (scenario, item, _) = await NewShopAsync();
        var saleLineId = FirstLineId(await PostSimpleCashSaleAsync(environment, scenario, item, 1));
        var viewOnly = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Invoice.SaleReturn.View);

        var response = await viewOnly.PostAsJsonAsync(SaleReturnsUrl, ReturnBody(scenario.FirstStoreId, [ReturnedLine(saleLineId, 1)]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ------------------------------------------------------------ exchange

    [Fact]
    public async Task An_exchange_returns_the_old_pair_and_sells_the_new_one_in_one_transaction_and_the_customer_pays_the_difference()
    {
        var (scenario, oldItem, lookups) = await NewShopAsync(1_000);
        var newItem = await CreateStockedItemAsync(environment, scenario, lookups, "Gents Loafer", 1_500, 5);
        var cashAccountId = await CashAccountIdAsync(environment, scenario.CompanyId);
        var customerId = await ContactTestHelpers.CreateCustomerAsync(scenario.OwnerClient, "Imran Traders");
        var oldLineId = FirstLineId(await PostSaleAsync(scenario.OwnerClient, SaleBody(
            scenario.FirstStoreId, [Line(oldItem.VariantId, 1, 1_000)], [CashPayment(cashAccountId, 1_000)], customerId: customerId)));

        var exchange = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, ExchangesUrl, ExchangeBody(
            scenario.FirstStoreId, [ReturnedLine(oldLineId, 1, "Wrong size")], [Line(newItem.VariantId, 1, 1_500)], [CashPayment(cashAccountId, 500)],
            customerId: customerId));

        var returnInvoice = exchange.GetProperty("returnInvoice");
        var saleInvoice = exchange.GetProperty("saleInvoice");
        Assert.Equal(-1_000, returnInvoice.GetProperty("netAmount").GetInt64());
        Assert.Equal(1_500, saleInvoice.GetProperty("netAmount").GetInt64());
        Assert.Equal(saleInvoice.GetProperty("id").GetGuid(), returnInvoice.GetProperty("relatedInvoiceId").GetGuid());
        Assert.Equal(returnInvoice.GetProperty("id").GetGuid(), saleInvoice.GetProperty("relatedInvoiceId").GetGuid());
        Assert.Equal(10, await StockOnHandAsync(environment, scenario.CompanyId, scenario.FirstStoreId, oldItem.VariantId));
        Assert.Equal(4, await StockOnHandAsync(environment, scenario.CompanyId, scenario.FirstStoreId, newItem.VariantId));
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));
        Assert.Equal(0, await CustomerNetBalanceAsync(environment, scenario.CompanyId, customerId));
    }

    [Fact]
    public async Task An_exchange_whose_new_item_is_out_of_stock_posts_neither_half()
    {
        var (scenario, oldItem, lookups) = await NewShopAsync(1_000);
        var newItem = await CreateStockedItemAsync(environment, scenario, lookups, "Gents Boot", 2_000, 0);
        var cashAccountId = await CashAccountIdAsync(environment, scenario.CompanyId);
        var oldLineId = FirstLineId(await PostSimpleCashSaleAsync(environment, scenario, oldItem, 1));
        var invoicesBefore = await InvoiceCountAsync(environment, scenario.CompanyId);
        var customerId = await ContactTestHelpers.CreateCustomerAsync(scenario.OwnerClient, "Nadeem Stores");

        var response = await scenario.OwnerClient.PostAsJsonAsync(ExchangesUrl, ExchangeBody(
            scenario.FirstStoreId, [ReturnedLine(oldLineId, 1)], [Line(newItem.VariantId, 1, 2_000)], [CashPayment(cashAccountId, 1_000)], customerId: customerId));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(invoicesBefore, await InvoiceCountAsync(environment, scenario.CompanyId));
        Assert.Equal(9, await StockOnHandAsync(environment, scenario.CompanyId, scenario.FirstStoreId, oldItem.VariantId));
    }

    // ------------------------------------------------------------ void and re-enter

    [Fact]
    public async Task Voiding_a_sale_puts_the_stock_back_keeps_the_invoice_and_lists_it_as_deleted()
    {
        var (scenario, item, _) = await NewShopAsync();
        var sale = await PostSimpleCashSaleAsync(environment, scenario, item, 2);
        var saleId = sale.GetProperty("id").GetGuid();

        var voided = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, VoidUrl(saleId), new { reason = "Wrong customer" });

        Assert.Equal(1, voided.GetProperty("voidedInvoiceCount").GetInt32());
        Assert.Equal("VOID", voided.GetProperty("invoice").GetProperty("statusCode").GetString());
        Assert.Equal("Wrong customer", voided.GetProperty("invoice").GetProperty("voidReason").GetString());
        Assert.Equal(10, await StockOnHandAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));

        var deleted = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(DeletedInvoicesUrl));
        Assert.Equal(1, deleted.GetProperty("totalCount").GetInt64());
        Assert.Equal(saleId, deleted.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await scenario.OwnerClient.GetAsync($"{DeletedInvoicesUrl}/{saleId}")).StatusCode);

        var again = await scenario.OwnerClient.PostAsJsonAsync(VoidUrl(saleId), new { reason = "Again" });
        Assert.False(again.IsSuccessStatusCode);
    }

    [Fact]
    public async Task A_sale_with_a_live_return_cannot_be_voided_until_the_return_is_voided()
    {
        var (scenario, item, _) = await NewShopAsync();
        var sale = await PostSimpleCashSaleAsync(environment, scenario, item, 2);
        var returned = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, SaleReturnsUrl,
            ReturnBody(scenario.FirstStoreId, [ReturnedLine(FirstLineId(sale), 1)]));

        var refused = await scenario.OwnerClient.PostAsJsonAsync(VoidUrl(sale.GetProperty("id").GetGuid()), new { reason = "Mistake" });
        Assert.False(refused.IsSuccessStatusCode);

        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, VoidUrl(returned.GetProperty("id").GetGuid()), new { reason = "Return was a mistake" });
        Assert.Equal(8, await StockOnHandAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, VoidUrl(sale.GetProperty("id").GetGuid()), new { reason = "Mistake" });
        Assert.Equal(10, await StockOnHandAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
    }

    [Fact]
    public async Task Void_and_re_enter_voids_the_old_sale_and_posts_the_corrected_one_that_points_back_at_it()
    {
        var (scenario, item, _) = await NewShopAsync();
        var cashAccountId = await CashAccountIdAsync(environment, scenario.CompanyId);
        var sale = await PostSimpleCashSaleAsync(environment, scenario, item, 2);

        var result = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, VoidAndReenterUrl(sale.GetProperty("id").GetGuid()), new
        {
            voidReason = "Quantity was wrong",
            correctedSale = SaleBody(scenario.FirstStoreId, [Line(item.VariantId, 1, 1_000)], [CashPayment(cashAccountId, 1_000)]),
        });

        Assert.Equal("VOID", result.GetProperty("voidedInvoice").GetProperty("statusCode").GetString());
        var corrected = result.GetProperty("correctedInvoice");
        Assert.Equal("POSTED", corrected.GetProperty("statusCode").GetString());
        Assert.Equal(1, corrected.GetProperty("netAmount").GetInt64() / 1_000);
        Assert.Equal(1, corrected.GetProperty("replacesInvoiceNumber").GetInt64());
        Assert.Equal(9, await StockOnHandAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));
    }

    [Fact]
    public async Task Voiding_needs_the_delete_permission_and_the_deleted_list_needs_its_own_permission()
    {
        var (scenario, item, _) = await NewShopAsync();
        var sale = await PostSimpleCashSaleAsync(environment, scenario, item, 1);
        var viewer = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Invoice.ViewInvoices.View);

        var voidResponse = await viewer.PostAsJsonAsync(VoidUrl(sale.GetProperty("id").GetGuid()), new { reason = "Not allowed" });
        Assert.Equal(HttpStatusCode.Forbidden, voidResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(DeletedInvoicesUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(InvoicesUrl)).StatusCode);
        Assert.Equal(9, await StockOnHandAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
    }
}
