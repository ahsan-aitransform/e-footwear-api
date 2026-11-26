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
/// New Sale (docx 5.1, Part 11): cash, credit and part-paid sales, the stock and the journal they leave behind, the refusals, the safe retry
/// (Part 23), the permission matrix and the isolation of companies. Money is whole rupees everywhere.
/// </summary>
[Collection(ApiIntegrationCollection.Name)]
public class SalePostingApiTests(ApiTestEnvironment environment)
{
    private async Task<(OwnerCompanyScenario Scenario, StockedItem Item, Guid CashAccountId)> NewShopAsync(long shelfPrice = 1_000, decimal stock = 10)
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var lookups = await CatalogueTestHelpers.CreateLookupsAsync(scenario.OwnerClient);
        var item = await CreateStockedItemAsync(environment, scenario, lookups, "Gents Sandal", shelfPrice, stock);
        return (scenario, item, await CashAccountIdAsync(environment, scenario.CompanyId));
    }

    [Fact]
    public async Task A_cash_sale_with_change_posts_the_invoice_takes_the_stock_out_and_balances_the_journal()
    {
        var (scenario, item, cashAccountId) = await NewShopAsync();

        var invoice = await PostSaleAsync(scenario.OwnerClient, SaleBody(
            scenario.FirstStoreId, [Line(item.VariantId, 2, 1_000)], [CashPayment(cashAccountId, 2_000)], cashReturned: 500));

        Assert.Equal("SALE", invoice.GetProperty("documentTypeCode").GetString());
        Assert.Equal(1, invoice.GetProperty("invoiceNumber").GetInt64());
        Assert.Equal(2_000, invoice.GetProperty("netAmount").GetInt64());
        Assert.Equal(2_500, invoice.GetProperty("paidAmount").GetInt64());
        Assert.Equal(500, invoice.GetProperty("cashReturnedAmount").GetInt64());
        Assert.Equal(0, invoice.GetProperty("outstandingAmount").GetInt64());
        Assert.Equal("POSTED", invoice.GetProperty("statusCode").GetString());
        Assert.Equal(8, await StockOnHandAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));
        Assert.Equal(2_000, await NetBalanceOfAccountAsync(environment, scenario.CompanyId, cashAccountId));
    }

    [Fact]
    public async Task A_credit_sale_goes_to_the_account_of_the_customer_and_a_part_paid_sale_leaves_the_rest_outstanding()
    {
        var (scenario, item, cashAccountId) = await NewShopAsync();
        var customerId = await ContactTestHelpers.CreateCustomerAsync(scenario.OwnerClient, "Haji Rafiq");

        var credit = await PostSaleAsync(scenario.OwnerClient, SaleBody(
            scenario.FirstStoreId, [Line(item.VariantId, 1, 1_000)], [], customerId: customerId));
        Assert.Equal(1_000, credit.GetProperty("outstandingAmount").GetInt64());
        Assert.Equal(1_000, await CustomerNetBalanceAsync(environment, scenario.CompanyId, customerId));

        var partPaid = await PostSaleAsync(scenario.OwnerClient, SaleBody(
            scenario.FirstStoreId, [Line(item.VariantId, 2, 1_000)], [CashPayment(cashAccountId, 500)], customerId: customerId));
        Assert.Equal(1_500, partPaid.GetProperty("outstandingAmount").GetInt64());
        Assert.Equal(2_500, await CustomerNetBalanceAsync(environment, scenario.CompanyId, customerId));
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));
    }

    [Fact]
    public async Task The_purchase_history_of_a_customer_lists_only_their_invoices_with_the_items_newest_first()
    {
        var (scenario, item, cashAccountId) = await NewShopAsync();
        var customerId = await ContactTestHelpers.CreateCustomerAsync(scenario.OwnerClient, "Haji Rafiq");
        await PostSaleAsync(scenario.OwnerClient, SaleBody(scenario.FirstStoreId, [Line(item.VariantId, 1, 1_000)], [], customerId: customerId));
        await PostSaleAsync(scenario.OwnerClient, SaleBody(scenario.FirstStoreId, [Line(item.VariantId, 2, 1_000)], [], customerId: customerId));
        await PostSaleAsync(scenario.OwnerClient, SaleBody(scenario.FirstStoreId, [Line(item.VariantId, 1, 1_000)], [CashPayment(cashAccountId, 1_000)]));

        var history = await OwnerCompanyScenario.ReadJsonAsync(
            await scenario.OwnerClient.GetAsync($"{NewSaleUrl}/customers/{customerId}/purchases?pageNumber=1&pageSize=10"));

        Assert.Equal(2, history.GetProperty("totalCount").GetInt32());
        var invoices = history.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(new long[] { 2, 1 }, invoices.Select(invoice => invoice.GetProperty("invoiceNumber").GetInt64()).ToArray());
        var line = Assert.Single(invoices[0].GetProperty("lines").EnumerateArray().ToArray());
        Assert.Equal("Gents Sandal", line.GetProperty("productName").GetString());
        Assert.Equal(2, line.GetProperty("quantity").GetDecimal());
        Assert.Equal(2_000, line.GetProperty("netAmount").GetInt64());
    }

    private static async Task<int> CustomerCountAsync(HttpClient client, string searchText) =>
        (await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync($"{ContactTestHelpers.CustomersUrl}?searchText={Uri.EscapeDataString(searchText)}")))
        .GetProperty("totalCount").GetInt32();

    [Fact]
    public async Task A_typed_name_and_mobile_save_a_customer_once_and_the_invoices_belong_to_them()
    {
        var (scenario, item, cashAccountId) = await NewShopAsync();
        object Sale(string name, string mobile, Guid? clientTransactionId = null) => SaleBody(
            scenario.FirstStoreId, [Line(item.VariantId, 1, 1_000)], [CashPayment(cashAccountId, 1_000)],
            clientTransactionId: clientTransactionId, walkInName: name, walkInMobile: mobile);
        var clientTransactionId = Guid.NewGuid();

        var first = await PostSaleAsync(scenario.OwnerClient, Sale("Bilal Ahmed", "0300 7654000", clientTransactionId));
        var retried = await PostSaleAsync(scenario.OwnerClient, Sale("Bilal Ahmed", "0300 7654000", clientTransactionId));
        var again = await PostSaleAsync(scenario.OwnerClient, Sale("bilal  ahmed", "0300-7654000"));

        var customerId = first.GetProperty("customerId").GetGuid();
        Assert.Equal("Bilal Ahmed", first.GetProperty("customerName").GetString());
        Assert.Equal(customerId, retried.GetProperty("customerId").GetGuid());
        Assert.Equal(customerId, again.GetProperty("customerId").GetGuid());
        Assert.Equal(1, await CustomerCountAsync(scenario.OwnerClient, "Bilal"));
        var customer = await ContactTestHelpers.GetCustomerAsync(scenario.OwnerClient, customerId);
        Assert.Equal("0300 7654000", customer.GetProperty("phone").GetString());
        Assert.Equal("REGULAR", customer.GetProperty("customerTypeCode").GetString());

        // another name on the same number is another person
        var relative = await PostSaleAsync(scenario.OwnerClient, Sale("Sara Ahmed", "0300 7654000"));
        Assert.NotEqual(customerId, relative.GetProperty("customerId").GetGuid());
    }

    [Fact]
    public async Task A_typed_name_without_a_mobile_stays_a_cash_sale()
    {
        var (scenario, item, cashAccountId) = await NewShopAsync();

        var invoice = await PostSaleAsync(scenario.OwnerClient, SaleBody(
            scenario.FirstStoreId, [Line(item.VariantId, 1, 1_000)], [CashPayment(cashAccountId, 1_000)], walkInName: "Only A Name"));

        Assert.Equal("Only A Name", invoice.GetProperty("walkInCustomerName").GetString());
        Assert.Equal(0, await CustomerCountAsync(scenario.OwnerClient, "Only A Name"));
    }

    [Theory]
    [InlineData("call me", 1)]
    [InlineData("0300 7654000", 99)]
    public async Task A_refused_sale_saves_neither_an_invoice_nor_the_typed_customer(string mobile, int quantity)
    {
        var (scenario, item, cashAccountId) = await NewShopAsync(stock: 2);

        var response = await scenario.OwnerClient.PostAsJsonAsync(NewSaleUrl, SaleBody(
            scenario.FirstStoreId, [Line(item.VariantId, quantity, 1_000)], [CashPayment(cashAccountId, 1_000 * quantity)],
            walkInName: "Never Saved", walkInMobile: mobile));

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(0, await InvoiceCountAsync(environment, scenario.CompanyId));
        Assert.Equal(0, await CustomerCountAsync(scenario.OwnerClient, "Never Saved"));
    }

    [Fact]
    public async Task A_credit_sale_without_a_customer_is_refused()
    {
        var (scenario, item, _) = await NewShopAsync();

        var response = await scenario.OwnerClient.PostAsJsonAsync(NewSaleUrl, SaleBody(scenario.FirstStoreId, [Line(item.VariantId, 1, 1_000)], []));

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(0, await InvoiceCountAsync(environment, scenario.CompanyId));
    }

    [Fact]
    public async Task A_sale_of_more_than_the_stock_is_refused_and_nothing_is_stored()
    {
        var (scenario, item, cashAccountId) = await NewShopAsync(stock: 2);

        var response = await scenario.OwnerClient.PostAsJsonAsync(NewSaleUrl, SaleBody(
            scenario.FirstStoreId, [Line(item.VariantId, 3, 1_000)], [CashPayment(cashAccountId, 3_000)]));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(0, await InvoiceCountAsync(environment, scenario.CompanyId));
        Assert.Equal(2, await StockOnHandAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
    }

    [Fact]
    public async Task A_price_above_the_shelf_price_and_a_payment_larger_than_needed_without_change_are_refused()
    {
        var (scenario, item, cashAccountId) = await NewShopAsync();

        var tooHigh = await scenario.OwnerClient.PostAsJsonAsync(NewSaleUrl, SaleBody(
            scenario.FirstStoreId, [Line(item.VariantId, 1, 1_200)], [CashPayment(cashAccountId, 1_200)]));
        Assert.False(tooHigh.IsSuccessStatusCode);

        var overpaid = await scenario.OwnerClient.PostAsJsonAsync(NewSaleUrl, SaleBody(
            scenario.FirstStoreId, [Line(item.VariantId, 1, 1_000)], [CashPayment(cashAccountId, 1_500)]));
        Assert.False(overpaid.IsSuccessStatusCode);
        Assert.Equal(0, await InvoiceCountAsync(environment, scenario.CompanyId));
    }

    [Fact]
    public async Task A_percent_discount_lowers_the_charged_price_and_a_discount_needs_the_permission()
    {
        var (scenario, item, cashAccountId) = await NewShopAsync();

        var invoice = await PostSaleAsync(scenario.OwnerClient, SaleBody(
            scenario.FirstStoreId, [Line(item.VariantId, 1, 1_000, "PERCENT", 10)], [CashPayment(cashAccountId, 900)]));
        Assert.Equal(900, invoice.GetProperty("netAmount").GetInt64());
        Assert.Equal(100, invoice.GetProperty("discountAmount").GetInt64());

        var cashier = await scenario.SignInManagerWithPermissionsAsync(
            PermissionCodes.Invoice.NewSale.View, PermissionCodes.Invoice.NewSale.Add, PermissionCodes.RestrictedOperations.Sales.SaleCreate);
        var refused = await cashier.PostAsJsonAsync(NewSaleUrl, SaleBody(
            scenario.FirstStoreId, [Line(item.VariantId, 1, 1_000, "PERCENT", 10)], [CashPayment(cashAccountId, 900)]));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("discount", await ProblemMessageAsync(refused), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_product_with_a_tax_rate_is_refused_because_the_tax_rule_is_not_decided()
    {
        var (scenario, item, cashAccountId) = await NewShopAsync();
        await environment.Database.ExecuteAsOwnerAsync("update catalog.products set tax_rate = 17 where company_id = $1 and id = $2", scenario.CompanyId, item.ProductId);

        var response = await scenario.OwnerClient.PostAsJsonAsync(NewSaleUrl, SaleBody(
            scenario.FirstStoreId, [Line(item.VariantId, 1, 1_000)], [CashPayment(cashAccountId, 1_000)]));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("tax", await ProblemMessageAsync(response), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sending_the_same_client_transaction_again_returns_the_same_invoice_and_takes_the_stock_out_once()
    {
        var (scenario, item, cashAccountId) = await NewShopAsync();
        var body = SaleBody(scenario.FirstStoreId, [Line(item.VariantId, 1, 1_000)], [CashPayment(cashAccountId, 1_000)], clientTransactionId: Guid.NewGuid());

        var first = await PostSaleAsync(scenario.OwnerClient, body);
        var second = await PostSaleAsync(scenario.OwnerClient, body);

        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal(1, await InvoiceCountAsync(environment, scenario.CompanyId));
        Assert.Equal(9, await StockOnHandAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
    }

    [Fact]
    public async Task Four_simultaneous_sends_of_the_same_client_transaction_make_one_invoice()
    {
        var (scenario, item, cashAccountId) = await NewShopAsync();
        var body = SaleBody(scenario.FirstStoreId, [Line(item.VariantId, 1, 1_000)], [CashPayment(cashAccountId, 1_000)], clientTransactionId: Guid.NewGuid());

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => scenario.OwnerClient.PostAsJsonAsync(NewSaleUrl, body)));

        Assert.All(responses, response => Assert.True(response.IsSuccessStatusCode, response.StatusCode.ToString()));
        var ids = (await Task.WhenAll(responses.Select(OwnerCompanyScenario.ReadJsonAsync))).Select(invoice => invoice.GetProperty("id").GetGuid()).Distinct().ToList();
        Assert.Single(ids);
        Assert.Equal(1, await InvoiceCountAsync(environment, scenario.CompanyId));
        Assert.Equal(9, await StockOnHandAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
    }

    [Fact]
    public async Task The_entry_context_and_the_barcode_lookup_give_the_next_numbers_and_the_price_of_the_item()
    {
        var (scenario, item, _) = await NewShopAsync();
        await PostSimpleCashSaleAsync(environment, scenario, item, 1);

        var context = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(
            $"{SalesUrl}/{E_Footwear_Api.Routing.ApiRoutes.Sales.NewSaleEntryContext}?storeId={scenario.FirstStoreId}&businessDate={TestSaleDate}"));
        Assert.Equal(2, context.GetProperty("nextInvoiceNumber").GetInt64());
        Assert.Equal(2, context.GetProperty("nextDailyBillNumber").GetInt64());
        Assert.Equal(1_000, context.GetProperty("todaySaleAmount").GetInt64());

        var found = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(
            $"{SalesUrl}/{E_Footwear_Api.Routing.ApiRoutes.Sales.NewSaleItemByBarcode}?storeId={scenario.FirstStoreId}&barcode={item.Barcode}"));
        Assert.Equal(1_000, found.GetProperty("suggestedUnitPrice").GetInt64());
        Assert.Equal(9, found.GetProperty("stockQuantity").GetDecimal());

        var missing = await scenario.OwnerClient.GetAsync(
            $"{SalesUrl}/{E_Footwear_Api.Routing.ApiRoutes.Sales.NewSaleItemByBarcode}?storeId={scenario.FirstStoreId}&barcode=NO-SUCH-CODE");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task A_user_without_the_add_permission_cannot_sell_and_a_user_without_the_view_permission_cannot_list_invoices()
    {
        var (scenario, item, cashAccountId) = await NewShopAsync();
        var viewOnly = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Invoice.NewSale.View);

        var sale = await viewOnly.PostAsJsonAsync(NewSaleUrl, SaleBody(scenario.FirstStoreId, [Line(item.VariantId, 1, 1_000)], [CashPayment(cashAccountId, 1_000)]));
        Assert.Equal(HttpStatusCode.Forbidden, sale.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewOnly.GetAsync(InvoicesUrl)).StatusCode);
        Assert.Equal(0, await InvoiceCountAsync(environment, scenario.CompanyId));
    }

    [Fact]
    public async Task A_sale_is_refused_for_a_store_the_user_does_not_work_in_and_another_company_cannot_see_the_invoice()
    {
        var (scenario, item, cashAccountId) = await NewShopAsync();
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        var invoice = await PostSimpleCashSaleAsync(environment, scenario, item, 1);

        var firstStoreOnly = await scenario.SignInManagerWithPermissionsAsync(
            PermissionCodes.Invoice.NewSale.View, PermissionCodes.Invoice.NewSale.Add, PermissionCodes.RestrictedOperations.Sales.SaleCreate);
        var otherStore = await firstStoreOnly.PostAsJsonAsync(NewSaleUrl, SaleBody(secondStoreId, [Line(item.VariantId, 1, 1_000)], [CashPayment(cashAccountId, 1_000)]));
        Assert.Equal(HttpStatusCode.Forbidden, otherStore.StatusCode);

        var otherCompany = await OwnerCompanyScenario.CreateAsync(environment);
        var peek = await otherCompany.OwnerClient.GetAsync(InvoiceUrl(invoice.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.NotFound, peek.StatusCode);
    }

    [Fact]
    public async Task The_invoice_list_filters_by_number_and_the_print_document_carries_the_header_and_the_invoice()
    {
        var (scenario, item, _) = await NewShopAsync();
        var first = await PostSimpleCashSaleAsync(environment, scenario, item, 1);
        await PostSimpleCashSaleAsync(environment, scenario, item, 2);

        var list = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{InvoicesUrl}?invoiceNumber=1"));
        Assert.Equal(1, list.GetProperty("totalCount").GetInt64());
        Assert.Equal(first.GetProperty("id").GetGuid(), list.GetProperty("items")[0].GetProperty("id").GetGuid());

        var all = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{InvoicesUrl}?minimumNetAmount=1500"));
        Assert.Equal(1, all.GetProperty("totalCount").GetInt64());

        var print = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{InvoiceUrl(first.GetProperty("id").GetGuid())}/print-document"));
        Assert.Equal(1, print.GetProperty("invoice").GetProperty("invoiceNumber").GetInt64());
        Assert.Equal("THERMAL", print.GetProperty("printDefaults").GetProperty("printFormatCode").GetString());
        Assert.False(string.IsNullOrEmpty(print.GetProperty("businessHeader").GetProperty("businessName").GetString()));
    }
}
