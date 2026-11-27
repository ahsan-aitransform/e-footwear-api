using System.Net;
using System.Net.Http.Json;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using static EFootwear.Tests.Promotions.PromotionTestHelpers;

namespace EFootwear.Tests.Promotions;

/// <summary>
/// Special offers, sales on stores, sales on categories, deals and promotion conflicts (docx 2.12 to 2.16, Part 18). The price a customer pays comes from the pricing engine, so these tests
/// read the price of a size in a store before and after each change. Two promotions on one size are never combined: the size stays at the normal price until somebody chooses.
/// </summary>
[Collection(ApiIntegrationCollection.Name)]
public class PromotionApiTests(ApiTestEnvironment environment)
{
    private static readonly string[] AllPromotionPermissions =
    [
        PermissionCodes.Settings.SpecialOffers.View, PermissionCodes.Settings.SpecialOffers.Add, PermissionCodes.Settings.SpecialOffers.Edit, PermissionCodes.Settings.SpecialOffers.Delete,
        PermissionCodes.Settings.SpecialOfferDetails.View, PermissionCodes.Settings.SpecialOfferDetails.Add, PermissionCodes.Settings.SpecialOfferDetails.Edit,
        PermissionCodes.Settings.SpecialOfferDetails.Delete, PermissionCodes.Settings.SaleOnStores.View, PermissionCodes.Settings.SaleOnStores.Add,
        PermissionCodes.Settings.SaleOnStores.Edit, PermissionCodes.Settings.SaleOnStores.Delete, PermissionCodes.Settings.SaleOnCategories.View,
        PermissionCodes.Settings.SaleOnCategories.Add, PermissionCodes.Settings.SaleOnCategories.Edit, PermissionCodes.Settings.SaleOnCategories.Delete,
        PermissionCodes.Settings.Deals.View, PermissionCodes.Settings.Deals.Add, PermissionCodes.Settings.Deals.Edit, PermissionCodes.Settings.Deals.Delete,
        PermissionCodes.Promotions.PromotionConflictResolution.View, PermissionCodes.Promotions.PromotionConflictResolution.Approve,
        PermissionCodes.RestrictedOperations.Promotions.PromoManage, PermissionCodes.RestrictedOperations.Promotions.PromoConflictResolve,
    ];

    // ---- Sale on Stores ----

    [Fact]
    public async Task A_sale_on_stores_discounts_only_the_chosen_store_and_stops_when_it_is_deleted()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;
        var secondStoreId = await shop.Scenario.CreateStoreAsync("S2");

        var sale = await CreateSaleOnStoresAsync(client, "Eid sale", 10, shop.Scenario.FirstStoreId);

        Assert.Equal("RUNNING", sale.GetProperty("phaseCode").GetString());
        Assert.Equal(10m, sale.GetProperty("discountPercent").GetDecimal());
        Assert.Equal(1, sale.GetProperty("storeCount").GetInt32());
        Assert.Equal(900, await PriceAtAsync(client, shop.FirstBarcode, shop.Scenario.FirstStoreId));
        Assert.Equal(1_000, await PriceAtAsync(client, shop.FirstBarcode, secondStoreId));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(SaleOnStoresUrl(sale.GetProperty("id").GetGuid()))).StatusCode);
        Assert.Equal(1_000, await PriceAtAsync(client, shop.FirstBarcode, shop.Scenario.FirstStoreId));
        Assert.Equal(0, (await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(SalesOnStoresUrl))).GetArrayLength());
    }

    [Fact]
    public async Task A_sale_can_cover_several_stores_and_changing_it_moves_the_discount()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;
        var secondStoreId = await shop.Scenario.CreateStoreAsync("S2");
        var sale = await CreateSaleOnStoresAsync(client, "Both stores", 20, shop.Scenario.FirstStoreId, secondStoreId);
        Assert.Equal(800, await PriceAtAsync(client, shop.FirstBarcode, secondStoreId));

        var changed = await client.PutAsJsonAsync(SaleOnStoresUrl(sale.GetProperty("id").GetGuid()),
            RuleBody("Second store only", 50, [secondStoreId], rowVersion: sale.GetProperty("rowVersion").GetInt64()));

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(1_000, await PriceAtAsync(client, shop.FirstBarcode, shop.Scenario.FirstStoreId));
        Assert.Equal(500, await PriceAtAsync(client, shop.FirstBarcode, secondStoreId));
    }

    [Fact]
    public async Task A_sale_that_has_not_started_or_is_switched_off_changes_no_price()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;
        var storeId = shop.Scenario.FirstStoreId;

        var future = await OwnerCompanyScenario.PostJsonAsync(client, SalesOnStoresUrl,
            RuleBody("Next week", 30, [storeId], startsAt: DateTimeOffset.UtcNow.AddDays(7), endsAt: DateTimeOffset.UtcNow.AddDays(14)));
        var off = await OwnerCompanyScenario.PostJsonAsync(client, SalesOnStoresUrl, RuleBody("Not active", 40, [storeId], isActive: false));
        var ended = await OwnerCompanyScenario.PostJsonAsync(client, SalesOnStoresUrl,
            RuleBody("Last month", 50, [storeId], startsAt: DateTimeOffset.UtcNow.AddDays(-20), endsAt: DateTimeOffset.UtcNow.AddDays(-10)));

        Assert.Equal("SCHEDULED", future.GetProperty("phaseCode").GetString());
        Assert.Equal("INACTIVE", off.GetProperty("phaseCode").GetString());
        Assert.Equal("ENDED", ended.GetProperty("phaseCode").GetString());
        Assert.Equal(1_000, await PriceAtAsync(client, shop.FirstBarcode, storeId));
    }

    [Fact]
    public async Task Switching_a_sale_on_and_off_changes_the_price()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;
        var sale = await OwnerCompanyScenario.PostJsonAsync(client, SalesOnStoresUrl, RuleBody("Toggle", 10, [shop.Scenario.FirstStoreId], isActive: false));
        var saleId = sale.GetProperty("id").GetGuid();
        Assert.Equal(1_000, await PriceAtAsync(client, shop.FirstBarcode, shop.Scenario.FirstStoreId));

        var on = await OwnerCompanyScenario.ReadJsonAsync(await client.PutAsJsonAsync(SaleOnStoresUrl(saleId),
            RuleBody("Toggle", 10, [shop.Scenario.FirstStoreId], isActive: true, rowVersion: sale.GetProperty("rowVersion").GetInt64())));
        Assert.Equal(900, await PriceAtAsync(client, shop.FirstBarcode, shop.Scenario.FirstStoreId));

        await client.PutAsJsonAsync(SaleOnStoresUrl(saleId),
            RuleBody("Toggle", 10, [shop.Scenario.FirstStoreId], isActive: false, rowVersion: on.GetProperty("rowVersion").GetInt64()));
        Assert.Equal(1_000, await PriceAtAsync(client, shop.FirstBarcode, shop.Scenario.FirstStoreId));
    }

    [Fact]
    public async Task A_sale_price_is_what_a_sale_is_posted_at()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;
        await CatalogueSeedStockAsync(shop, shop.FirstVariantId);
        await CreateSaleOnStoresAsync(client, "Eid sale", 10, shop.Scenario.FirstStoreId);
        var cashAccountId = await Sales.SalesTestHelpers.CashAccountIdAsync(environment, shop.Scenario.CompanyId);

        var invoice = await Sales.SalesTestHelpers.PostSaleAsync(client, Sales.SalesTestHelpers.SaleBody(
            shop.Scenario.FirstStoreId, [Sales.SalesTestHelpers.Line(shop.FirstVariantId, 2, 900)], [Sales.SalesTestHelpers.CashPayment(cashAccountId, 1_800)]));

        Assert.Equal(1_800, invoice.GetProperty("netAmount").GetInt64());
    }

    // ---- Sale on Categories ----

    [Fact]
    public async Task A_sale_on_categories_discounts_only_products_of_those_categories_in_the_chosen_store()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;
        var other = await AddProductInAnotherCategoryAsync(shop);
        var secondStoreId = await shop.Scenario.CreateStoreAsync("S2");

        var sale = await OwnerCompanyScenario.PostJsonAsync(client, SalesOnCategoriesUrl,
            RuleBody("Casual clearance", 25, [shop.Scenario.FirstStoreId], [shop.Lookups.CategoryId]));

        Assert.Equal("Shoes", sale.GetProperty("categories")[0].GetProperty("departmentName").GetString());
        Assert.Equal("Casual", sale.GetProperty("categories")[0].GetProperty("categoryName").GetString());
        Assert.Equal(750, await PriceAtAsync(client, shop.FirstBarcode, shop.Scenario.FirstStoreId));
        Assert.Equal(2_000, await PriceAtAsync(client, other.Barcode, shop.Scenario.FirstStoreId));
        Assert.Equal(1_000, await PriceAtAsync(client, shop.FirstBarcode, secondStoreId));
    }

    [Fact]
    public async Task A_sale_on_categories_can_change_its_categories()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;
        var other = await AddProductInAnotherCategoryAsync(shop);
        var sale = await OwnerCompanyScenario.PostJsonAsync(client, SalesOnCategoriesUrl, RuleBody("Casual", 10, [shop.Scenario.FirstStoreId], [shop.Lookups.CategoryId]));

        var changed = await OwnerCompanyScenario.ReadJsonAsync(await client.PutAsJsonAsync(SaleOnCategoriesUrl(sale.GetProperty("id").GetGuid()),
            RuleBody("Formal", 10, [shop.Scenario.FirstStoreId], [other.CategoryId], rowVersion: sale.GetProperty("rowVersion").GetInt64())));

        Assert.Equal("Formal", changed.GetProperty("categories")[0].GetProperty("categoryName").GetString());
        Assert.Equal(1_000, await PriceAtAsync(client, shop.FirstBarcode, shop.Scenario.FirstStoreId));
        Assert.Equal(1_800, await PriceAtAsync(client, other.Barcode, shop.Scenario.FirstStoreId));
    }

    [Fact]
    public async Task The_category_picker_lists_every_category_with_its_department_and_needs_the_screen_permission()
    {
        var shop = await NewShopAsync(environment);
        var other = await AddProductInAnotherCategoryAsync(shop);
        var url = $"{SalesOnCategoriesUrl}/category-options";
        var nobody = await shop.Scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [shop.Scenario.FirstStoreId]);

        var options = await OwnerCompanyScenario.ReadJsonAsync(await shop.Scenario.OwnerClient.GetAsync(url));

        Assert.Equal(2, options.GetArrayLength());
        Assert.Contains(options.EnumerateArray(), option => option.GetProperty("categoryId").GetGuid() == other.CategoryId && option.GetProperty("categoryName").GetString() == "Formal"
                                                              && option.GetProperty("departmentName").GetString() == "Shoes");
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync(url)).StatusCode);
    }

    // ---- Special Offers ----

    [Fact]
    public async Task A_special_offer_discounts_each_product_by_its_own_percentage_in_every_store()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;
        var secondStoreId = await shop.Scenario.CreateStoreAsync("S2");
        var offer = await CreateSpecialOfferAsync(client, "Weekend offer");
        var offerId = offer.GetProperty("id").GetGuid();
        Assert.Equal(0, offer.GetProperty("storeCount").GetInt32());

        var added = await AddOfferLinesAsync(client, offerId, shop.FirstBarcode, null, null, 20);
        Assert.Equal(1, added.GetProperty("addedCount").GetInt32());
        await AddOfferLinesAsync(client, offerId, shop.SecondBarcode, null, null, 30);

        Assert.Equal(800, await PriceAtAsync(client, shop.FirstBarcode, shop.Scenario.FirstStoreId));
        Assert.Equal(800, await PriceAtAsync(client, shop.FirstBarcode, secondStoreId));
        Assert.Equal(700, await PriceAtAsync(client, shop.SecondBarcode, shop.Scenario.FirstStoreId));
        var lines = await ListOfferLinesAsync(client, offerId);
        Assert.Equal(2, lines.GetArrayLength());
        Assert.Equal("Casual", lines[0].GetProperty("categoryName").GetString());
        Assert.Equal("Bata", lines[0].GetProperty("brandName").GetString());
    }

    [Fact]
    public async Task A_special_offer_line_can_be_changed_and_removed_and_a_size_is_in_an_offer_once()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;
        var offerId = (await CreateSpecialOfferAsync(client, "Offer")).GetProperty("id").GetGuid();
        await AddOfferLinesAsync(client, offerId, shop.FirstBarcode, null, null, 20);

        var again = await AddOfferLinesAsync(client, offerId, shop.FirstBarcode, null, null, 50);
        Assert.Equal(0, again.GetProperty("addedCount").GetInt32());
        Assert.Equal(1, again.GetProperty("skippedCount").GetInt32());
        var lineId = (await ListOfferLinesAsync(client, offerId))[0].GetProperty("lineId").GetGuid();
        Assert.Equal(800, await PriceAtAsync(client, shop.FirstBarcode, shop.Scenario.FirstStoreId));   // the first percentage stays

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(SpecialOfferLineUrl(offerId, lineId), new { discountPercent = 40m })).StatusCode);
        Assert.Equal(600, await PriceAtAsync(client, shop.FirstBarcode, shop.Scenario.FirstStoreId));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(SpecialOfferLineUrl(offerId, lineId))).StatusCode);
        Assert.Equal(1_000, await PriceAtAsync(client, shop.FirstBarcode, shop.Scenario.FirstStoreId));
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(SpecialOfferLineUrl(offerId, lineId))).StatusCode);
    }

    [Fact]
    public async Task All_products_of_a_category_or_a_company_can_be_added_to_an_offer_at_once()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;
        var other = await AddProductInAnotherCategoryAsync(shop);
        var offerId = (await CreateSpecialOfferAsync(client, "Offer")).GetProperty("id").GetGuid();

        var byCategory = await AddOfferLinesAsync(client, offerId, null, other.CategoryId, null, 10);
        Assert.Equal(1, byCategory.GetProperty("addedCount").GetInt32());
        var byBrand = await AddOfferLinesAsync(client, offerId, null, null, shop.Lookups.BrandId, 20);
        Assert.Equal(2, byBrand.GetProperty("addedCount").GetInt32());   // the two sizes of Gents Sandal; the formal shoe is already in
        Assert.Equal(1, byBrand.GetProperty("skippedCount").GetInt32());

        Assert.Equal(3, (await ListOfferLinesAsync(client, offerId)).GetArrayLength());
        Assert.Equal(1_800, await PriceAtAsync(client, other.Barcode, shop.Scenario.FirstStoreId));
        Assert.Equal(800, await PriceAtAsync(client, shop.SecondBarcode, shop.Scenario.FirstStoreId));
    }

    [Fact]
    public async Task An_offer_line_is_checked_before_it_is_added()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;
        var offerId = (await CreateSpecialOfferAsync(client, "Offer")).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(SpecialOfferLinesUrl(offerId), new { barcode = "NO-SUCH-CODE", discountPercent = 10m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(SpecialOfferLinesUrl(offerId), new { discountPercent = 10m })).StatusCode);   // nothing chosen
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(SpecialOfferLinesUrl(offerId),
            new { barcode = shop.FirstBarcode, categoryId = shop.Lookups.CategoryId, discountPercent = 10m })).StatusCode);   // two sources
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(SpecialOfferLinesUrl(offerId), new { barcode = shop.FirstBarcode, discountPercent = 0m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(SpecialOfferLinesUrl(offerId), new { barcode = shop.FirstBarcode, discountPercent = 100.5m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(SpecialOfferLinesUrl(offerId), new { barcode = shop.FirstBarcode, discountPercent = 12.345m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(SpecialOfferLinesUrl(offerId), new { barcode = shop.FirstBarcode })).StatusCode);
        Assert.Equal(0, (await ListOfferLinesAsync(client, offerId)).GetArrayLength());
    }

    [Fact]
    public async Task A_special_offer_for_chosen_stores_reaches_only_those_stores()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;
        var secondStoreId = await shop.Scenario.CreateStoreAsync("S2");
        var offer = await CreateSpecialOfferAsync(client, "First store offer", shop.Scenario.FirstStoreId);
        await AddOfferLinesAsync(client, offer.GetProperty("id").GetGuid(), shop.FirstBarcode, null, null, 20);

        Assert.Equal(1, offer.GetProperty("storeCount").GetInt32());
        Assert.Equal(800, await PriceAtAsync(client, shop.FirstBarcode, shop.Scenario.FirstStoreId));
        Assert.Equal(1_000, await PriceAtAsync(client, shop.FirstBarcode, secondStoreId));
    }

    // ---- Conflicts: no silent stacking ----

    [Fact]
    public async Task Two_promotions_on_one_size_are_not_combined_and_the_size_stays_at_the_normal_price_until_somebody_chooses()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;
        var storeId = shop.Scenario.FirstStoreId;
        var sale = await CreateSaleOnStoresAsync(client, "Eid sale", 10, storeId);
        var offer = await CreateSpecialOfferAsync(client, "Offer");
        await AddOfferLinesAsync(client, offer.GetProperty("id").GetGuid(), shop.FirstBarcode, null, null, 20);

        Assert.Equal(1_000, await PriceAtAsync(client, shop.FirstBarcode, storeId));            // conflict: normal price
        Assert.Equal(900, await PriceAtAsync(client, shop.SecondBarcode, storeId));             // only the sale covers this size
        var conflicts = await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(ConflictsUrl));
        Assert.Equal(1, conflicts.GetProperty("totalCount").GetInt32());
        var conflict = conflicts.GetProperty("items")[0];
        Assert.Equal(shop.FirstBarcode, conflict.GetProperty("barcode").GetString());
        Assert.Equal(1_000, conflict.GetProperty("listPrice").GetInt64());
        var candidates = conflict.GetProperty("candidates").EnumerateArray().ToList();
        Assert.Equal([800L, 900L], candidates.Select(candidate => candidate.GetProperty("salePrice").GetInt64()).Order().ToList());

        var chosenRuleId = candidates.Single(candidate => candidate.GetProperty("kindCode").GetString() == "SPECIAL_OFFER").GetProperty("ruleId").GetGuid();
        var resolve = await client.PostAsJsonAsync(ResolveConflictUrl(conflict.GetProperty("groupId").GetGuid()), new { chosenRuleId });
        Assert.Equal(HttpStatusCode.NoContent, resolve.StatusCode);
        Assert.Equal(800, await PriceAtAsync(client, shop.FirstBarcode, storeId));
        Assert.Equal(0, (await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(ConflictsUrl))).GetProperty("totalCount").GetInt32());

        // Ending the sale removes the overlap, so the offer alone prices the size.
        await client.DeleteAsync(SaleOnStoresUrl(sale.GetProperty("id").GetGuid()));
        Assert.Equal(800, await PriceAtAsync(client, shop.FirstBarcode, storeId));
    }

    [Fact]
    public async Task A_conflict_can_only_be_resolved_with_one_of_its_own_promotions_and_only_once()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;
        await CreateSaleOnStoresAsync(client, "Sale one", 10, shop.Scenario.FirstStoreId);
        await CreateSaleOnStoresAsync(client, "Sale two", 20, shop.Scenario.FirstStoreId);
        var conflict = (await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(ConflictsUrl))).GetProperty("items")[0];
        var groupId = conflict.GetProperty("groupId").GetGuid();
        var chosenRuleId = conflict.GetProperty("candidates")[0].GetProperty("ruleId").GetGuid();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(ResolveConflictUrl(groupId), new { chosenRuleId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(ResolveConflictUrl(groupId), new { chosenRuleId })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(ResolveConflictUrl(groupId), new { chosenRuleId })).StatusCode);
    }

    [Fact]
    public async Task A_new_promotion_on_a_resolved_size_opens_the_question_again()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;
        var storeId = shop.Scenario.FirstStoreId;
        await CreateSaleOnStoresAsync(client, "Sale one", 10, storeId);
        await CreateSaleOnStoresAsync(client, "Sale two", 20, storeId);
        var conflict = (await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(ConflictsUrl))).GetProperty("items")[0];
        await client.PostAsJsonAsync(ResolveConflictUrl(conflict.GetProperty("groupId").GetGuid()), new { chosenRuleId = conflict.GetProperty("candidates")[0].GetProperty("ruleId").GetGuid() });
        Assert.NotEqual(1_000, await PriceAtAsync(client, shop.FirstBarcode, storeId));

        await CreateSaleOnStoresAsync(client, "Sale three", 30, storeId);

        Assert.Equal(1_000, await PriceAtAsync(client, shop.FirstBarcode, storeId));
        Assert.True((await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(ConflictsUrl))).GetProperty("totalCount").GetInt32() >= 1);
    }

    // ---- Deals ----

    [Fact]
    public async Task A_deal_gets_its_own_barcode_and_its_price_is_the_sum_of_its_lines()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;

        var deal = await OwnerCompanyScenario.PostJsonAsync(client, DealsUrl,
            DealBody("Two pairs deal", [DealLine(shop.FirstVariantId, 2, 800), DealLine(shop.SecondVariantId, 1, 900)]));

        Assert.StartsWith("DL", deal.GetProperty("barcode").GetString());
        Assert.Equal(2_500, deal.GetProperty("bundlePrice").GetInt64());
        Assert.Equal(2, deal.GetProperty("lineCount").GetInt32());
        Assert.Equal(3m, deal.GetProperty("totalQuantity").GetDecimal());
        Assert.Equal("RUNNING", deal.GetProperty("phaseCode").GetString());
        var line = deal.GetProperty("lines").EnumerateArray().Single(candidate => candidate.GetProperty("variantId").GetGuid() == shop.FirstVariantId);
        Assert.Equal(1_000, line.GetProperty("salePrice").GetInt64());
        Assert.Equal(800, line.GetProperty("netUnitPrice").GetInt64());
        Assert.Equal(1_600, line.GetProperty("amount").GetInt64());

        var second = await OwnerCompanyScenario.PostJsonAsync(client, DealsUrl, DealBody("Another deal", [DealLine(shop.FirstVariantId, 1, 700)]));
        Assert.NotEqual(deal.GetProperty("barcode").GetString(), second.GetProperty("barcode").GetString());
    }

    [Fact]
    public async Task A_deal_can_be_changed_with_its_row_version_and_two_people_cannot_overwrite_each_other()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;
        var deal = await OwnerCompanyScenario.PostJsonAsync(client, DealsUrl, DealBody("Deal", [DealLine(shop.FirstVariantId, 1, 800)]));
        var dealId = deal.GetProperty("id").GetGuid();
        var version = deal.GetProperty("rowVersion").GetInt64();

        var changed = await OwnerCompanyScenario.ReadJsonAsync(await client.PutAsJsonAsync(DealUrl(dealId),
            DealBody("Renamed", [DealLine(shop.SecondVariantId, 3, 500)], deal.GetProperty("barcode").GetString(), rowVersion: version)));
        Assert.Equal("Renamed", changed.GetProperty("name").GetString());
        Assert.Equal(1_500, changed.GetProperty("bundlePrice").GetInt64());
        Assert.Equal(deal.GetProperty("barcode").GetString(), changed.GetProperty("barcode").GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(DealUrl(dealId), DealBody("Stale", [DealLine(shop.FirstVariantId, 1, 1)], rowVersion: version))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(DealUrl(dealId), DealBody("No version", [DealLine(shop.FirstVariantId, 1, 1)]))).StatusCode);
    }

    [Fact]
    public async Task A_deal_barcode_can_never_be_the_barcode_of_a_size_or_of_another_deal()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;
        var lines = new[] { DealLine(shop.FirstVariantId, 1, 800) };

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(DealsUrl, DealBody("Clash", lines, shop.FirstBarcode))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(DealsUrl, DealBody("Mine", lines, "PROMO-1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(DealsUrl, DealBody("Twin", lines, "PROMO-1"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(DealsUrl, DealBody("Bad", lines, "has space"))).StatusCode);
    }

    [Fact]
    public async Task A_deal_is_checked_before_it_is_saved()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(DealsUrl, DealBody("", [DealLine(shop.FirstVariantId, 1, 800)]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(DealsUrl, DealBody("No lines", []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(DealsUrl, DealBody("Same size twice", [DealLine(shop.FirstVariantId, 1, 800), DealLine(shop.FirstVariantId, 1, 700)]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(DealsUrl, DealBody("Half a pair", [DealLine(shop.FirstVariantId, 1.5m, 800)]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(DealsUrl, DealBody("Zero quantity", [DealLine(shop.FirstVariantId, 0, 800)]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(DealsUrl, DealBody("Negative price", [DealLine(shop.FirstVariantId, 1, -1)]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(DealsUrl, DealBody("Unknown size", [DealLine(Guid.NewGuid(), 1, 800)]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(DealsUrl,
            DealBody("Backwards", [DealLine(shop.FirstVariantId, 1, 800)], startsAt: DateTimeOffset.UtcNow, endsAt: DateTimeOffset.UtcNow.AddDays(-1)))).StatusCode);
        Assert.Equal(0, (await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(DealsUrl))).GetArrayLength());
    }

    [Fact]
    public async Task A_scanned_barcode_finds_its_size_and_current_price_for_a_deal_and_a_deleted_deal_leaves_the_list()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;

        var found = await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(DealVariantUrl(shop.FirstBarcode)));
        Assert.Equal(shop.FirstVariantId, found.GetProperty("variantId").GetGuid());
        Assert.Equal(1_000, found.GetProperty("salePrice").GetInt64());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(DealVariantUrl("NO-SUCH-CODE"))).StatusCode);

        var deal = await OwnerCompanyScenario.PostJsonAsync(client, DealsUrl, DealBody("Deal", [DealLine(shop.FirstVariantId, 1, 800)]));
        var dealId = deal.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(DealUrl(dealId))).StatusCode);
        Assert.Equal(0, (await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(DealsUrl))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(DealUrl(dealId))).StatusCode);
        // the barcode of a deleted deal can be used again
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(DealsUrl, DealBody("Again", [DealLine(shop.FirstVariantId, 1, 800)], deal.GetProperty("barcode").GetString()))).StatusCode);
    }

    // ---- Checks on every kind ----

    [Fact]
    public async Task A_promotion_is_checked_before_it_is_saved()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;
        var storeId = shop.Scenario.FirstStoreId;

        async Task AssertBadRequestAsync(string url, object body) => Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(url, body)).StatusCode);

        await AssertBadRequestAsync(SalesOnStoresUrl, RuleBody("", 10, [storeId]));                                              // no name
        await AssertBadRequestAsync(SalesOnStoresUrl, RuleBody("No percentage", null, [storeId]));
        await AssertBadRequestAsync(SalesOnStoresUrl, RuleBody("Zero", 0, [storeId]));
        await AssertBadRequestAsync(SalesOnStoresUrl, RuleBody("Too much", 100.5m, [storeId]));
        await AssertBadRequestAsync(SalesOnStoresUrl, RuleBody("Decimals", 12.345m, [storeId]));
        await AssertBadRequestAsync(SalesOnStoresUrl, RuleBody("No store", 10));
        await AssertBadRequestAsync(SalesOnStoresUrl, RuleBody("Backwards", 10, [storeId], startsAt: DateTimeOffset.UtcNow, endsAt: DateTimeOffset.UtcNow.AddDays(-1)));
        await AssertBadRequestAsync(SalesOnCategoriesUrl, RuleBody("No category", 10, [storeId]));
        await AssertBadRequestAsync(SalesOnCategoriesUrl, RuleBody("Unknown category", 10, [storeId], [Guid.NewGuid()]));
        await AssertBadRequestAsync(SalesOnCategoriesUrl, RuleBody("No store", 10, null, [shop.Lookups.CategoryId]));
        await AssertBadRequestAsync(SpecialOffersUrl, RuleBody("", null));
        await AssertBadRequestAsync(SpecialOffersUrl, RuleBody("Backwards", null, startsAt: DateTimeOffset.UtcNow, endsAt: DateTimeOffset.UtcNow.AddDays(-1)));
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(SalesOnStoresUrl, RuleBody("Strange store", 10, [Guid.NewGuid()]))).StatusCode);
        Assert.Equal(0, (await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(SalesOnStoresUrl))).GetArrayLength());
    }

    [Fact]
    public async Task Two_people_changing_the_same_sale_cannot_overwrite_each_other()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;
        var sale = await CreateSaleOnStoresAsync(client, "Sale", 10, shop.Scenario.FirstStoreId);
        var saleId = sale.GetProperty("id").GetGuid();
        var version = sale.GetProperty("rowVersion").GetInt64();

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(SaleOnStoresUrl(saleId), RuleBody("First", 20, [shop.Scenario.FirstStoreId], rowVersion: version))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(SaleOnStoresUrl(saleId), RuleBody("Second", 30, [shop.Scenario.FirstStoreId], rowVersion: version))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(SaleOnStoresUrl(saleId), RuleBody("No version", 30, [shop.Scenario.FirstStoreId]))).StatusCode);
        Assert.Equal("First", (await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(SaleOnStoresUrl(saleId)))).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Each_screen_reaches_only_its_own_kind_of_promotion()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;
        var sale = await CreateSaleOnStoresAsync(client, "Sale", 10, shop.Scenario.FirstStoreId);
        var offer = await CreateSpecialOfferAsync(client, "Offer");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(SpecialOfferUrl(sale.GetProperty("id").GetGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(SaleOnStoresUrl(offer.GetProperty("id").GetGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(SaleOnCategoriesUrl(sale.GetProperty("id").GetGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(SpecialOfferLinesUrl(sale.GetProperty("id").GetGuid()))).StatusCode);
        Assert.Equal(1, (await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(SalesOnStoresUrl))).GetArrayLength());
        Assert.Equal(1, (await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(SpecialOffersUrl))).GetArrayLength());
    }

    [Fact]
    public async Task A_deleted_promotion_stays_in_the_database_but_never_in_a_list_or_a_price()
    {
        var shop = await NewShopAsync(environment);
        var client = shop.Scenario.OwnerClient;
        var sale = await CreateSaleOnStoresAsync(client, "Sale", 10, shop.Scenario.FirstStoreId);
        var saleId = sale.GetProperty("id").GetGuid();

        await client.DeleteAsync(SaleOnStoresUrl(saleId));

        Assert.Equal(1, await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from promo.rules where company_id = $1 and id = $2 and is_deleted and status = 'CANCELLED'", shop.Scenario.CompanyId, saleId));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(SaleOnStoresUrl(saleId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(SaleOnStoresUrl(saleId))).StatusCode);
    }

    // ---- Permissions, stores, companies ----

    [Fact]
    public async Task Every_promotion_screen_needs_its_own_permission_and_a_change_also_needs_the_restricted_code()
    {
        var shop = await NewShopAsync(environment);
        var scenario = shop.Scenario;
        var offer = await CreateSpecialOfferAsync(scenario.OwnerClient, "Offer", scenario.FirstStoreId);
        var nobody = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId]);
        var viewer = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId],
            PermissionCodes.Settings.SpecialOffers.View, PermissionCodes.Settings.SaleOnStores.View, PermissionCodes.Settings.Deals.View);
        var screenOnly = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId],
            PermissionCodes.Settings.SaleOnStores.View, PermissionCodes.Settings.SaleOnStores.Add);
        var restrictedOnly = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId],
            PermissionCodes.RestrictedOperations.Promotions.PromoManage);
        var allowed = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId], AllPromotionPermissions);
        var body = RuleBody("Sale", 10, [scenario.FirstStoreId]);

        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync(SpecialOffersUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync(SalesOnStoresUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync(SalesOnCategoriesUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync(DealsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync(ConflictsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync(SpecialOfferLinesUrl(offer.GetProperty("id").GetGuid()))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(SpecialOffersUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(DealsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(SalesOnCategoriesUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(SpecialOfferLinesUrl(offer.GetProperty("id").GetGuid()))).StatusCode);   // the details screen is separate
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(SalesOnStoresUrl, body)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await screenOnly.PostAsJsonAsync(SalesOnStoresUrl, body)).StatusCode);        // without PROMO_MANAGE
        Assert.Equal(HttpStatusCode.Forbidden, (await restrictedOnly.PostAsJsonAsync(SalesOnStoresUrl, body)).StatusCode);    // without the screen's Add
        Assert.Equal(0, (await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(SalesOnStoresUrl))).GetArrayLength());

        Assert.Equal(HttpStatusCode.Created, (await allowed.PostAsJsonAsync(SalesOnStoresUrl, body)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await allowed.PostAsync(RefreshPricesUrl, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await screenOnly.PostAsync(RefreshPricesUrl, null)).StatusCode);
    }

    [Fact]
    public async Task Only_the_owner_chooses_the_winner_of_a_conflict()
    {
        var shop = await NewShopAsync(environment);
        var scenario = shop.Scenario;
        await CreateSaleOnStoresAsync(scenario.OwnerClient, "Sale one", 10, scenario.FirstStoreId);
        await CreateSaleOnStoresAsync(scenario.OwnerClient, "Sale two", 20, scenario.FirstStoreId);
        var conflict = (await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(ConflictsUrl))).GetProperty("items")[0];
        var url = ResolveConflictUrl(conflict.GetProperty("groupId").GetGuid());
        var body = new { chosenRuleId = conflict.GetProperty("candidates")[0].GetProperty("ruleId").GetGuid() };
        var manager = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId],
            PermissionCodes.Promotions.PromotionConflictResolution.View, PermissionCodes.Promotions.PromotionConflictResolution.Approve,
            PermissionCodes.RestrictedOperations.Promotions.PromoConflictResolve);

        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync(ConflictsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync(url, body)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await scenario.OwnerClient.PostAsJsonAsync(url, body)).StatusCode);
    }

    [Fact]
    public async Task Nobody_but_the_owner_makes_or_removes_a_promotion_whatever_was_granted()
    {
        var shop = await NewShopAsync(environment);
        var scenario = shop.Scenario;
        var manager = await scenario.SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [scenario.FirstStoreId], AllPromotionPermissions);
        var sale = await CreateSaleOnStoresAsync(scenario.OwnerClient, "Owner sale", 10, scenario.FirstStoreId);

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync(SalesOnStoresUrl, RuleBody("My store", 10, [scenario.FirstStoreId]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync(SpecialOffersUrl, RuleBody("My store offer", null, [scenario.FirstStoreId]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.DeleteAsync(SaleOnStoresUrl(sale.GetProperty("id").GetGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync(DealsUrl, DealBody("Deal", [DealLine(shop.FirstVariantId, 1, 800)]))).StatusCode);
    }

    [Fact]
    public async Task Another_company_never_sees_or_changes_the_promotions_of_this_company()
    {
        var shop = await NewShopAsync(environment);
        var sale = await CreateSaleOnStoresAsync(shop.Scenario.OwnerClient, "Sale", 10, shop.Scenario.FirstStoreId);
        await OwnerCompanyScenario.PostJsonAsync(shop.Scenario.OwnerClient, DealsUrl, DealBody("Deal", [DealLine(shop.FirstVariantId, 1, 800)]));
        var strangers = await OwnerCompanyScenario.CreateAsync(environment);

        Assert.Equal(0, (await OwnerCompanyScenario.ReadJsonAsync(await strangers.OwnerClient.GetAsync(SalesOnStoresUrl))).GetArrayLength());
        Assert.Equal(0, (await OwnerCompanyScenario.ReadJsonAsync(await strangers.OwnerClient.GetAsync(DealsUrl))).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await strangers.OwnerClient.GetAsync(SaleOnStoresUrl(sale.GetProperty("id").GetGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await strangers.OwnerClient.DeleteAsync(SaleOnStoresUrl(sale.GetProperty("id").GetGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await strangers.OwnerClient.PostAsJsonAsync(SalesOnStoresUrl, RuleBody("Steal", 10, [shop.Scenario.FirstStoreId]))).StatusCode);   // another company's store
        Assert.Equal(900, await PriceAtAsync(shop.Scenario.OwnerClient, shop.FirstBarcode, shop.Scenario.FirstStoreId));
    }

    private Task CatalogueSeedStockAsync(Shop shop, Guid variantId) =>
        Catalogue.CatalogueTestHelpers.SeedStockAsync(environment, shop.Scenario.CompanyId, shop.Scenario.FirstStoreId, variantId, 10);
}
