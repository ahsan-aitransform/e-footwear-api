using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Catalogue;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using static EFootwear.Tests.Accounting.VoucherTestHelpers;
using static EFootwear.Tests.Inventory.InventoryTestHelpers;

namespace EFootwear.Tests.Inventory;

/// <summary>
/// Stock Adjustment, the non-zero stock count update and the void (Part 10, docx 5.7 and 5.8): stock and the journal after each, the refusals, the safe retry
/// (Part 23), two people adjusting at once, the permission matrix and the isolation of companies and stores.
/// </summary>
[Collection(ApiIntegrationCollection.Name)]
public class StockAdjustmentApiTests(ApiTestEnvironment environment)
{
    private async Task<(OwnerCompanyScenario Scenario, InventoryItem Item)> NewShopAsync(decimal stock = 10)
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var lookups = await LookupsAsync(scenario);
        return (scenario, await CreateItemAsync(environment, scenario, lookups, "Gents Sandal", stock));
    }

    [Fact]
    public async Task Adding_pieces_raises_the_stock_numbers_the_document_and_balances_the_journal()
    {
        var (scenario, item) = await NewShopAsync();

        var adjustment = await PostAdjustmentAsync(scenario.OwnerClient, scenario.FirstStoreId, item.VariantId, 5);

        Assert.Equal("POSTED", adjustment.GetProperty("statusCode").GetString());
        Assert.Equal("MANUAL", adjustment.GetProperty("kindCode").GetString());
        Assert.Equal(1, adjustment.GetProperty("documentNumber").GetInt64());
        Assert.Equal(5, adjustment.GetProperty("addedQuantity").GetDecimal());
        Assert.Equal(0, adjustment.GetProperty("removedQuantity").GetDecimal());
        Assert.Equal(2_000, adjustment.GetProperty("netValueAmount").GetInt64());
        Assert.Equal(15, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));
        Assert.Equal(1, await JournalEntryCountAsync(environment, scenario.CompanyId, "STOCK_ADJUSTMENT"));

        var second = await PostAdjustmentAsync(scenario.OwnerClient, scenario.FirstStoreId, item.VariantId, 1);
        Assert.Equal(2, second.GetProperty("documentNumber").GetInt64());
    }

    [Fact]
    public async Task Removing_pieces_lowers_the_stock_and_removing_more_than_is_there_is_refused_with_the_quantity_in_stock()
    {
        var (scenario, item) = await NewShopAsync(stock: 4);

        var removed = await PostAdjustmentAsync(scenario.OwnerClient, scenario.FirstStoreId, item.VariantId, -3);
        Assert.Equal(3, removed.GetProperty("removedQuantity").GetDecimal());
        Assert.Equal(1, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));

        var refused = await scenario.OwnerClient.PostAsJsonAsync(AdjustmentsUrl, AdjustmentBody(scenario.FirstStoreId, [AdjustmentLine(item.VariantId, -2)]));
        Assert.False(refused.IsSuccessStatusCode);
        Assert.Contains("only 1", await ProblemMessageAsync(refused));
        Assert.Equal(1, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
        Assert.Equal(1, await DocumentCountAsync(environment, scenario.CompanyId, "ADJUSTMENT"));
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));
    }

    [Fact]
    public async Task Sending_the_same_transaction_id_twice_returns_the_same_document_and_moves_the_stock_once()
    {
        var (scenario, item) = await NewShopAsync();
        var transactionId = Guid.NewGuid();
        var body = AdjustmentBody(scenario.FirstStoreId, [AdjustmentLine(item.VariantId, 3)], transactionId);

        var first = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, AdjustmentsUrl, body);
        var second = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, AdjustmentsUrl, body);

        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal(13, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
        Assert.Equal(1, await DocumentCountAsync(environment, scenario.CompanyId, "ADJUSTMENT"));
    }

    [Fact]
    public async Task Two_people_removing_most_of_the_stock_at_the_same_moment_cannot_take_more_than_is_there()
    {
        var (scenario, item) = await NewShopAsync(stock: 10);

        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => scenario.OwnerClient.PostAsJsonAsync(
            AdjustmentsUrl, AdjustmentBody(scenario.FirstStoreId, [AdjustmentLine(item.VariantId, -6)]))));

        Assert.Equal(1, responses.Count(response => response.IsSuccessStatusCode));
        Assert.Equal(4, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));
    }

    [Fact]
    public async Task Voiding_an_adjustment_adds_a_reversal_restores_the_stock_and_cannot_be_done_twice()
    {
        var (scenario, item) = await NewShopAsync();
        var adjustment = await PostAdjustmentAsync(scenario.OwnerClient, scenario.FirstStoreId, item.VariantId, -4);
        var adjustmentId = adjustment.GetProperty("id").GetGuid();
        Assert.Equal(6, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));

        var voided = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, $"{AdjustmentUrl(adjustmentId)}/void", new { reason = "Counted wrongly", clientTransactionId = Guid.NewGuid() });

        Assert.Equal("CANCELLED", voided.GetProperty("original").GetProperty("statusCode").GetString());
        Assert.Equal("REVERSAL", voided.GetProperty("reversal").GetProperty("kindCode").GetString());
        Assert.Equal(adjustmentId, voided.GetProperty("reversal").GetProperty("reversesDocumentId").GetGuid());
        Assert.Equal(10, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
        Assert.Equal(0, await JournalDifferenceAsync(environment, scenario.CompanyId));

        var again = await scenario.OwnerClient.PostAsJsonAsync($"{AdjustmentUrl(adjustmentId)}/void", new { reason = "Again", clientTransactionId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(10, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));

        var noReason = await scenario.OwnerClient.PostAsJsonAsync($"{AdjustmentUrl(adjustmentId)}/void", new { reason = " ", clientTransactionId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
    }

    [Fact]
    public async Task A_void_is_refused_when_the_pieces_of_an_increase_are_no_longer_in_stock()
    {
        var (scenario, item) = await NewShopAsync(stock: 0);
        var adjustment = await PostAdjustmentAsync(scenario.OwnerClient, scenario.FirstStoreId, item.VariantId, 5);
        await PostAdjustmentAsync(scenario.OwnerClient, scenario.FirstStoreId, item.VariantId, -4);

        var voided = await scenario.OwnerClient.PostAsJsonAsync($"{AdjustmentUrl(adjustment.GetProperty("id").GetGuid())}/void", new { reason = "Mistake", clientTransactionId = Guid.NewGuid() });

        Assert.False(voided.IsSuccessStatusCode);
        Assert.Equal(1, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
        Assert.Equal("POSTED", (await GetJsonAsync(scenario.OwnerClient, AdjustmentUrl(adjustment.GetProperty("id").GetGuid()))).GetProperty("statusCode").GetString());
    }

    [Theory]
    [InlineData("zero quantity")]
    [InlineData("repeated size")]
    [InlineData("no lines")]
    [InlineData("future date")]
    [InlineData("negative price")]
    public async Task Adjustments_with_wrong_input_are_refused_and_nothing_is_stored(string problem)
    {
        var (scenario, item) = await NewShopAsync();
        var body = problem switch
        {
            "zero quantity" => AdjustmentBody(scenario.FirstStoreId, [AdjustmentLine(item.VariantId, 0)]),
            "repeated size" => AdjustmentBody(scenario.FirstStoreId, [AdjustmentLine(item.VariantId, 1), AdjustmentLine(item.VariantId, 2)]),
            "no lines" => AdjustmentBody(scenario.FirstStoreId, []),
            "future date" => AdjustmentBody(scenario.FirstStoreId, [AdjustmentLine(item.VariantId, 1)], date: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)).ToString("yyyy-MM-dd")),
            _ => AdjustmentBody(scenario.FirstStoreId, [AdjustmentLine(item.VariantId, 1, unitCost: -5)]),
        };

        var response = await scenario.OwnerClient.PostAsJsonAsync(AdjustmentsUrl, body);

        Assert.False(response.IsSuccessStatusCode, problem);
        Assert.True((int)response.StatusCode is 400 or 422, $"{problem}: {response.StatusCode}");
        Assert.Equal(0, await DocumentCountAsync(environment, scenario.CompanyId, "ADJUSTMENT"));
        Assert.Equal(10, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
    }

    [Fact]
    public async Task The_list_filters_by_store_status_and_kind_and_the_detail_shows_every_line()
    {
        var (scenario, item) = await NewShopAsync();
        var first = await PostAdjustmentAsync(scenario.OwnerClient, scenario.FirstStoreId, item.VariantId, 2);
        await PostAdjustmentAsync(scenario.OwnerClient, scenario.FirstStoreId, item.VariantId, -1);
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, $"{AdjustmentUrl(first.GetProperty("id").GetGuid())}/void", new { reason = "Wrong", clientTransactionId = Guid.NewGuid() });

        var all = await GetJsonAsync(scenario.OwnerClient, $"{AdjustmentsUrl}?storeId={scenario.FirstStoreId}");
        Assert.Equal(3, all.GetProperty("totalCount").GetInt64());
        var cancelled = await GetJsonAsync(scenario.OwnerClient, $"{AdjustmentsUrl}?statusCode=CANCELLED");
        Assert.Equal(1, cancelled.GetProperty("totalCount").GetInt64());
        var reversals = await GetJsonAsync(scenario.OwnerClient, $"{AdjustmentsUrl}?kindCode=REVERSAL");
        Assert.Equal(1, reversals.GetProperty("totalCount").GetInt64());

        var detail = await GetJsonAsync(scenario.OwnerClient, AdjustmentUrl(first.GetProperty("id").GetGuid()));
        var line = detail.GetProperty("lines")[0];
        Assert.Equal(item.Barcode, line.GetProperty("barcode").GetString());
        Assert.Equal(2, line.GetProperty("quantity").GetDecimal());
        Assert.Equal(400, line.GetProperty("unitCostAmount").GetInt64());
    }

    [Fact]
    public async Task A_count_update_posts_only_the_differences_and_refuses_a_count_that_equals_the_books()
    {
        var (scenario, item) = await NewShopAsync(stock: 10);
        var lookups = await LookupsAsync(scenario);
        var other = await CreateItemAsync(environment, scenario, lookups, "Kids Shoe", 7);

        var update = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, CountUpdatesUrl, new
        {
            storeId = scenario.FirstStoreId, updateDate = TestDate, clientTransactionId = Guid.NewGuid(), description = "Monthly count",
            lines = new[] { new { variantId = item.VariantId, countedQuantity = 8m }, new { variantId = other.VariantId, countedQuantity = 7m } },
        });

        Assert.Equal("COUNT_UPDATE", update.GetProperty("kindCode").GetString());
        Assert.Equal(1, update.GetProperty("lines").GetArrayLength());
        var line = update.GetProperty("lines")[0];
        Assert.Equal(10, line.GetProperty("expectedQuantity").GetDecimal());
        Assert.Equal(8, line.GetProperty("countedQuantity").GetDecimal());
        Assert.Equal(-2, line.GetProperty("quantity").GetDecimal());
        Assert.Equal(8, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
        Assert.Equal(7, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, other.VariantId));

        var same = await scenario.OwnerClient.PostAsJsonAsync(CountUpdatesUrl, new
        {
            storeId = scenario.FirstStoreId, updateDate = TestDate, clientTransactionId = Guid.NewGuid(), description = (string?)null,
            lines = new[] { new { variantId = item.VariantId, countedQuantity = 8m } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, same.StatusCode);
    }

    [Fact]
    public async Task The_non_zero_stock_list_shows_only_sizes_with_stock_and_a_count_file_is_checked_row_by_row()
    {
        var (scenario, item) = await NewShopAsync(stock: 6);
        var lookups = await LookupsAsync(scenario);
        await CreateItemAsync(environment, scenario, lookups, "Empty Shelf", 0);

        var list = await GetJsonAsync(scenario.OwnerClient, $"{NonZeroStockUrl}?storeId={scenario.FirstStoreId}");
        Assert.Equal(1, list.GetProperty("totalCount").GetInt64());
        Assert.Equal(6, list.GetProperty("items")[0].GetProperty("stockQuantity").GetDecimal());

        var csv = $"Barcode,Count\n{item.Barcode},4\n9999999999999,3\n{item.Barcode},5\n";
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "count.csv");
        var preview = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.PostAsync($"{CountFilePreviewUrl}?storeId={scenario.FirstStoreId}", form));

        Assert.Equal(1, preview.GetProperty("rows").GetArrayLength());
        Assert.Equal(6, preview.GetProperty("rows")[0].GetProperty("bookQuantity").GetDecimal());
        Assert.Equal(4, preview.GetProperty("rows")[0].GetProperty("countedQuantity").GetDecimal());
        Assert.Equal(2, preview.GetProperty("problems").GetArrayLength());
        Assert.Equal(6, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
    }

    [Fact]
    public async Task The_permission_matrix_view_only_cannot_adjust_and_the_legacy_stock_adjust_code_is_also_needed()
    {
        var (scenario, item) = await NewShopAsync();
        var viewOnly = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Invoice.StockAdjustment.View);
        var noLegacy = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Invoice.StockAdjustment.View, PermissionCodes.Invoice.StockAdjustment.Add);
        var allowed = await scenario.SignInManagerWithPermissionsAsync(
            PermissionCodes.Invoice.StockAdjustment.View, PermissionCodes.Invoice.StockAdjustment.Add, PermissionCodes.RestrictedOperations.Stock.StockAdjust);
        var body = AdjustmentBody(scenario.FirstStoreId, [AdjustmentLine(item.VariantId, 1)]);

        Assert.Equal(HttpStatusCode.OK, (await viewOnly.GetAsync(AdjustmentsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewOnly.PostAsJsonAsync(AdjustmentsUrl, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await noLegacy.PostAsJsonAsync(AdjustmentsUrl, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await allowed.PostAsJsonAsync(AdjustmentsUrl, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await allowed.GetAsync(NonZeroStockUrl + $"?storeId={scenario.FirstStoreId}")).StatusCode);
        Assert.Equal(1, await DocumentCountAsync(environment, scenario.CompanyId, "ADJUSTMENT"));
    }

    [Fact]
    public async Task A_user_of_another_store_and_another_company_cannot_adjust_or_read_the_adjustment()
    {
        var (scenario, item) = await NewShopAsync();
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        var adjustment = await PostAdjustmentAsync(scenario.OwnerClient, scenario.FirstStoreId, item.VariantId, 1);
        var adjustmentId = adjustment.GetProperty("id").GetGuid();

        var secondStoreUser = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [secondStoreId],
            PermissionCodes.Invoice.StockAdjustment.View, PermissionCodes.Invoice.StockAdjustment.Add, PermissionCodes.RestrictedOperations.Stock.StockAdjust);
        Assert.Equal(HttpStatusCode.Forbidden, (await secondStoreUser.PostAsJsonAsync(AdjustmentsUrl, AdjustmentBody(scenario.FirstStoreId, [AdjustmentLine(item.VariantId, 1)]))).StatusCode);
        Assert.True((await secondStoreUser.GetAsync(AdjustmentUrl(adjustmentId))).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);

        var otherCompany = await OwnerCompanyScenario.CreateAsync(environment);
        Assert.Equal(HttpStatusCode.NotFound, (await otherCompany.OwnerClient.GetAsync(AdjustmentUrl(adjustmentId))).StatusCode);
        var foreignVariant = await otherCompany.OwnerClient.PostAsJsonAsync(AdjustmentsUrl, AdjustmentBody(otherCompany.FirstStoreId, [AdjustmentLine(item.VariantId, 1)]));
        Assert.False(foreignVariant.IsSuccessStatusCode);
        Assert.Equal(11, await StockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, item.VariantId));
    }
}
