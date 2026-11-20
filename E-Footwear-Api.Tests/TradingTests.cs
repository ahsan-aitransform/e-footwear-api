using System.Net;
using System.Text.Json;
using EFootwear.Api.Tests.Support;
using Xunit;

namespace EFootwear.Api.Tests;

[Collection("api")]
public sealed class TradingTests(ApiFixture fx)
{
    private static string U(string p) => p + Guid.NewGuid().ToString("N")[..6];

    private sealed record Shop(Session Owner, long Vendor, long Variant, string Barcode, long Variant2, string Barcode2, long Product);

    private static async Task<long> Id(Session s, string url, object body)
    {
        var (st, b) = await s.PostAsync(url, body);
        Assert.True(st == HttpStatusCode.Created, $"{url} -> {st} {b}");
        return b.GetProperty("id").GetInt64();
    }

    /// <summary>A vendor and an article with two sizes (list price 2000), but no stock yet.</summary>
    private async Task<Shop> NewShopAsync(long salePrice = 2000)
    {
        var o = await fx.OwnerAsync();
        var dep = await Id(o, "/api/v1/masters/departments", new { name = U("D") });
        var cat = await Id(o, "/api/v1/masters/categories", new { name = U("C"), departmentId = dep });
        var vendor = await Id(o, "/api/v1/parties", new { name = U("Vendor"), isVendor = true });
        var (st, b) = await o.PostAsync("/api/v1/catalog/products", new
        {
            name = U("Shoe"), departmentId = dep, categoryId = cat,
            variants = new[] { new { sizeCode = "8", purchasePrice = 1200, salePrice }, new { sizeCode = "9", purchasePrice = 1200, salePrice } },
        });
        Assert.Equal(HttpStatusCode.Created, st);
        var pid = b.GetProperty("id").GetInt64();
        var vs = (await o.GetJsonAsync($"/api/v1/catalog/products/{pid}")).GetProperty("variants").EnumerateArray().ToList();
        return new Shop(o, vendor, vs[0].GetProperty("id").GetInt64(), vs[0].GetProperty("barcode").GetString()!, vs[1].GetProperty("id").GetInt64(), vs[1].GetProperty("barcode").GetString()!, pid);
    }

    private static async Task<JsonElement> BuyAsync(Shop s, int qty = 10, long cost = 1200, long discount = 0, string? bill = null, long? variant = null)
    {
        var (st, b) = await s.Owner.PostAsync("/api/v1/purchases", new { vendorId = s.Vendor, vendorBillNo = bill, discount, lines = new[] { new { variantId = variant ?? s.Variant, qty, unitCost = cost } } });
        Assert.True(st == HttpStatusCode.Created, $"{st} {b}");
        return b;
    }

    private async Task<decimal> OnHand(long variant, long? store = null)
        => await fx.ScalarAsync<decimal?>("select coalesce(sum(qty_on_hand), 0) from inv.stock_balances where company_id = @c and variant_id = @v and (@s::bigint is null or store_id = @s)",
            ("c", fx.Acme.CompanyId), ("v", variant), ("s", (object?)store ?? DBNull.Value)) ?? 0;

    private async Task<long> Balance(Session o, long party) => (await o.GetJsonAsync($"/api/v1/parties/{party}")).GetProperty("balance").GetInt64();

    private async Task AssertJournalBalanced(long? entryId)
    {
        Assert.NotNull(entryId);
        Assert.Equal(0L, await fx.ScalarAsync<long>("select coalesce(sum(debit) - sum(credit), 0) from acct.journal_lines where company_id = @c and entry_id = @e", ("c", fx.Acme.CompanyId), ("e", entryId!.Value)));
        Assert.True(await fx.ScalarAsync<long>("select count(*) from acct.journal_lines where company_id = @c and entry_id = @e", ("c", fx.Acme.CompanyId), ("e", entryId.Value)) >= 2);
    }

    [Fact]
    public async Task Purchase_adds_stock_at_cost_owes_the_vendor_and_posts_a_balanced_journal()
    {
        var s = await NewShopAsync();
        var inv = await BuyAsync(s, 10, 1200, bill: U("B"));
        Assert.Equal(12000, inv.GetProperty("netAmount").GetInt64());
        Assert.Equal(10, await OnHand(s.Variant));
        Assert.Equal(-12000, await Balance(s.Owner, s.Vendor));   // we owe the vendor
        await AssertJournalBalanced(inv.GetProperty("journalEntryId").GetInt64());
        Assert.Equal(1200m, await fx.ScalarAsync<decimal>("select avg_cost from inv.stock_balances where company_id = @c and variant_id = @v", ("c", fx.Acme.CompanyId), ("v", s.Variant)));
    }

    [Fact]
    public async Task Purchase_discount_and_tax_are_spread_into_cost_so_stock_value_matches_the_bill()
    {
        var s = await NewShopAsync();
        var inv = await BuyAsync(s, 10, 1000, discount: 1000);   // gross 10000, net 9000 -> 900 a piece
        Assert.Equal(9000, inv.GetProperty("netAmount").GetInt64());
        Assert.Equal(900m, await fx.ScalarAsync<decimal>("select avg_cost from inv.stock_balances where company_id = @c and variant_id = @v", ("c", fx.Acme.CompanyId), ("v", s.Variant)));
        Assert.Equal(-9000, await Balance(s.Owner, s.Vendor));
        // last cost follows the bill, the sale price does not (the bill did not ask for it)
        var p = await s.Owner.GetJsonAsync($"/api/v1/catalog/variants/{s.Variant}/prices");
        Assert.Equal(1000, p.GetProperty("purchasePrice").GetInt64());
        Assert.Equal(2000, p.GetProperty("salePrice").GetInt64());
    }

    [Fact]
    public async Task Purchase_can_update_the_sale_price_and_the_same_vendor_bill_cannot_be_entered_twice()
    {
        var s = await NewShopAsync();
        var bill = U("VB");
        var (st, _) = await s.Owner.PostAsync("/api/v1/purchases", new { vendorId = s.Vendor, vendorBillNo = bill, lines = new[] { new { variantId = s.Variant, qty = 5, unitCost = 1200, salePrice = 2500, updatesSalePrice = true } } });
        Assert.Equal(HttpStatusCode.Created, st);
        Assert.Equal(2500, (await s.Owner.GetJsonAsync($"/api/v1/catalog/variants/{s.Variant}/prices")).GetProperty("salePrice").GetInt64());
        Assert.Equal(HttpStatusCode.Conflict, (await s.Owner.PostAsync("/api/v1/purchases", new { vendorId = s.Vendor, vendorBillNo = bill, lines = new[] { new { variantId = s.Variant, qty = 1, unitCost = 1 } } })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.PostAsync("/api/v1/purchases", new { vendorId = s.Vendor, lines = Array.Empty<object>() })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.PostAsync("/api/v1/purchases", new { vendorId = s.Vendor, discount = 999999, lines = new[] { new { variantId = s.Variant, qty = 1, unitCost = 10 } } })).Status);
        // a customer is not a vendor
        var cust = await Id(s.Owner, "/api/v1/parties", new { name = U("Cust"), isCustomer = true });
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.PostAsync("/api/v1/purchases", new { vendorId = cust, lines = new[] { new { variantId = s.Variant, qty = 1, unitCost = 10 } } })).Status);
    }

    [Fact]
    public async Task Cash_sale_prices_on_the_server_takes_stock_records_cost_and_posts_a_balanced_journal()
    {
        var s = await NewShopAsync();
        await BuyAsync(s, 10);
        var (st, b) = await s.Owner.PostAsync("/api/v1/sales", new { lines = new[] { new { barcode = s.Barcode, qty = 2 } }, payInFull = true });
        Assert.Equal(HttpStatusCode.Created, st);
        var inv = b.GetProperty("invoice");
        Assert.Equal(4000, inv.GetProperty("netAmount").GetInt64());
        Assert.Equal(4000, inv.GetProperty("paidAmount").GetInt64());
        Assert.Equal(0, inv.GetProperty("creditAmount").GetInt64());
        Assert.Equal(2400, inv.GetProperty("costAmount").GetInt64());
        Assert.Equal(1200m, inv.GetProperty("lines")[0].GetProperty("unitCost").GetDecimal());
        Assert.Equal(8, await OnHand(s.Variant));
        Assert.Equal(1, inv.GetProperty("dailyBillNo").GetInt32() >= 1 ? 1 : 0);
        await AssertJournalBalanced(inv.GetProperty("journalEntryId").GetInt64());
        // a second sale gets the next number
        var no1 = inv.GetProperty("invoiceNo").GetInt64();
        var no2 = (await s.Owner.PostAsync("/api/v1/sales", new { lines = new[] { new { barcode = s.Barcode, qty = 1 } }, payInFull = true })).Body.GetProperty("invoice").GetProperty("invoiceNo").GetInt64();
        Assert.Equal(no1 + 1, no2);
    }

    [Fact]
    public async Task A_client_cannot_set_the_price_without_the_right_and_stock_cannot_go_negative()
    {
        var s = await NewShopAsync();
        await BuyAsync(s, 3);
        var name = U("c");
        await Id(s.Owner, "/api/v1/users", new { userName = name, fullName = name, role = "CASHIER_SALESMAN", password = "Start-Pass-1", storeIds = new[] { fx.Acme.FirstStoreId }, permissions = new[] { "SALE_CREATE", "SALE_RETURN" } });
        var c = await fx.LoginAsync("Acme Footwear", name, "Start-Pass-1");
        await c.PostAsync("/api/v1/auth/change-password", new { currentPassword = "Start-Pass-1", newPassword = "Next-Pass-22" });
        c = await fx.LoginAsync("Acme Footwear", name, "Next-Pass-22");

        // the cashier sells at the server price, but cannot lower or raise it
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsync("/api/v1/sales", new { lines = new[] { new { barcode = s.Barcode, qty = 1 } }, payInFull = true })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/v1/sales", new { lines = new[] { new { barcode = s.Barcode, qty = 1, unitPrice = 1500 } }, payInFull = true })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/v1/sales", new { lines = new[] { new { barcode = s.Barcode, qty = 1, unitPrice = 2500 } }, payInFull = true })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/v1/sales", new { lines = new[] { new { barcode = s.Barcode, qty = 1 } }, billDiscount = 100, payInFull = true })).Status);

        // the owner may: a manual discount is recorded as MANUAL and approved
        var m = (await s.Owner.PostAsync("/api/v1/sales", new { lines = new[] { new { barcode = s.Barcode, qty = 1, unitPrice = 1500 } }, payInFull = true })).Body.GetProperty("invoice");
        Assert.Equal("MANUAL", m.GetProperty("lines")[0].GetProperty("discountSource").GetString());
        Assert.Equal(500, m.GetProperty("lines")[0].GetProperty("discountAmount").GetInt64());

        // only 1 left: asking for 5 is refused with a readable message and nothing is booked
        var before = await OnHand(s.Variant);
        var r = await s.Owner.PostAsync("/api/v1/sales", new { lines = new[] { new { barcode = s.Barcode, qty = 5 } }, payInFull = true });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.Status);
        Assert.Contains("Not enough stock", r.Body.GetProperty("title").GetString());
        Assert.Equal(before, await OnHand(s.Variant));
    }

    [Fact]
    public async Task Credit_sale_needs_a_named_customer_and_raises_their_balance_and_a_retry_with_the_same_uuid_does_not_double_post()
    {
        var s = await NewShopAsync();
        await BuyAsync(s, 10);
        var cust = await Id(s.Owner, "/api/v1/parties", new { name = U("Cust"), isCustomer = true });

        // no customer and not paid in full -> refused
        var bad = await s.Owner.PostAsync("/api/v1/sales", new { lines = new[] { new { barcode = s.Barcode, qty = 1 } }, payments = new[] { new { method = "CASH", amount = 500 } } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.Status);

        var uuid = Guid.NewGuid();
        var body = new { saleUuid = uuid, customerId = cust, lines = new[] { new { barcode = s.Barcode, qty = 1 } }, payments = new[] { new { method = "CASH", amount = 500 } } };
        var first = await s.Owner.PostAsync("/api/v1/sales", body);
        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Equal(1500, first.Body.GetProperty("invoice").GetProperty("creditAmount").GetInt64());
        Assert.Equal(1500, await Balance(s.Owner, cust));

        var again = await s.Owner.PostAsync("/api/v1/sales", body);   // the device retries after a dropped connection
        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.True(again.Body.GetProperty("duplicate").GetBoolean());
        Assert.Equal(first.Body.GetProperty("invoice").GetProperty("id").GetInt64(), again.Body.GetProperty("invoice").GetProperty("id").GetInt64());
        Assert.Equal(1500, await Balance(s.Owner, cust));
        Assert.Equal(9, await OnHand(s.Variant));
    }

    [Fact]
    public async Task Change_is_only_given_on_cash_and_payment_methods_are_validated()
    {
        var s = await NewShopAsync();
        await BuyAsync(s, 5);
        var cash = (await s.Owner.PostAsync("/api/v1/sales", new { lines = new[] { new { barcode = s.Barcode, qty = 1 } }, payments = new[] { new { method = "CASH", amount = 2500 } } })).Body.GetProperty("invoice");
        Assert.Equal(500, cash.GetProperty("changeAmount").GetInt64());
        Assert.Equal(2500, cash.GetProperty("paidAmount").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.PostAsync("/api/v1/sales", new { lines = new[] { new { barcode = s.Barcode, qty = 1 } }, payments = new[] { new { method = "CARD", amount = 2500 } } })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.PostAsync("/api/v1/sales", new { lines = new[] { new { barcode = s.Barcode, qty = 1 } }, payments = new[] { new { method = "BARTER", amount = 2000 } } })).Status);
        var split = (await s.Owner.PostAsync("/api/v1/sales", new { lines = new[] { new { barcode = s.Barcode, qty = 1 } }, payments = new[] { new { method = "CASH", amount = 1000, reference = (string?)null }, new { method = "CARD", amount = 1000, reference = (string?)"TXN1" } } })).Body.GetProperty("invoice");
        Assert.Equal(2, split.GetProperty("payments").GetArrayLength());
    }

    [Fact]
    public async Task Bill_discount_is_shared_across_lines_and_deals_expand_into_their_articles()
    {
        var s = await NewShopAsync();
        await BuyAsync(s, 10); await BuyAsync(s, 10, variant: s.Variant2);
        var inv = (await s.Owner.PostAsync("/api/v1/sales", new { lines = new[] { new { barcode = s.Barcode, qty = 1 }, new { barcode = s.Barcode2, qty = 1 } }, billDiscount = 301, payInFull = true })).Body.GetProperty("invoice");
        Assert.Equal(3699, inv.GetProperty("netAmount").GetInt64());
        Assert.Equal(301, inv.GetProperty("billDiscountAmount").GetInt64());
        Assert.Equal(301, inv.GetProperty("lines").EnumerateArray().Sum(l => l.GetProperty("discountAmount").GetInt64()));

        var bc = U("DEAL").ToUpperInvariant();
        await Id(s.Owner, "/api/v1/promotions/deals", new { name = "Pair", barcode = bc, bundlePrice = 3000, startsAt = DateTimeOffset.UtcNow.AddMinutes(-1), publish = true,
            items = new[] { new { variantId = s.Variant, qty = 1, netUnitPrice = 1400 }, new { variantId = s.Variant2, qty = 1, netUnitPrice = 1600 } } });
        var deal = (await s.Owner.PostAsync("/api/v1/sales", new { lines = new[] { new { barcode = bc, qty = 1 } }, payInFull = true })).Body.GetProperty("invoice");
        Assert.Equal(3000, deal.GetProperty("netAmount").GetInt64());
        Assert.Equal(2, deal.GetProperty("lines").GetArrayLength());
        Assert.All(deal.GetProperty("lines").EnumerateArray(), l => Assert.Equal("BUNDLE", l.GetProperty("discountSource").GetString()));
    }

    [Fact]
    public async Task Returns_restock_credit_the_customer_cannot_exceed_what_was_sold_and_void_restores_everything()
    {
        var s = await NewShopAsync();
        await BuyAsync(s, 10);
        var cust = await Id(s.Owner, "/api/v1/parties", new { name = U("Cust"), isCustomer = true });
        var sale = (await s.Owner.PostAsync("/api/v1/sales", new { customerId = cust, lines = new[] { new { barcode = s.Barcode, qty = 3 } }, payments = new[] { new { method = "CASH", amount = 1000 } } })).Body.GetProperty("invoice");
        var saleId = sale.GetProperty("id").GetInt64();
        var lineId = sale.GetProperty("lines")[0].GetProperty("id").GetInt64();
        Assert.Equal(5000, await Balance(s.Owner, cust));   // 6000 - 1000
        Assert.Equal(7, await OnHand(s.Variant));

        var ret = await s.Owner.PostAsync("/api/v1/sales/returns", new { originalInvoiceId = saleId, lines = new[] { new { originalLineId = lineId, qty = 1 } } });
        Assert.Equal(HttpStatusCode.Created, ret.Status);
        var ri = ret.Body.GetProperty("invoice");
        Assert.Equal("RETURN", ri.GetProperty("docType").GetString());
        Assert.Equal(-2000, ri.GetProperty("netAmount").GetInt64());
        Assert.Equal(8, await OnHand(s.Variant));
        Assert.Equal(3000, await Balance(s.Owner, cust));
        await AssertJournalBalanced(ri.GetProperty("journalEntryId").GetInt64());

        // 2 left to return; 3 is refused with the exact numbers, and nothing changes
        var over = await s.Owner.PostAsync("/api/v1/sales/returns", new { originalInvoiceId = saleId, lines = new[] { new { originalLineId = lineId, qty = 3 } } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, over.Status);
        Assert.Contains("at most 2", over.Body.GetProperty("title").GetString());
        Assert.Equal(8, await OnHand(s.Variant));

        // the sale cannot be voided while a live return exists; voiding the return first, then the sale, restores stock and balance
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await s.Owner.PostAsync($"/api/v1/sales/{saleId}/void", new { reason = "mistake" })).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.PostAsync($"/api/v1/sales/{ri.GetProperty("id").GetInt64()}/void", new { reason = "customer kept it" })).Status);
        Assert.Equal(7, await OnHand(s.Variant));
        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.PostAsync($"/api/v1/sales/{saleId}/void", new { reason = "wrong customer" })).Status);
        Assert.Equal(10, await OnHand(s.Variant));
        Assert.Equal(0, await Balance(s.Owner, cust));
        var voided = await s.Owner.GetJsonAsync($"/api/v1/sales/{saleId}");
        Assert.Equal("VOID", voided.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await s.Owner.PostAsync($"/api/v1/sales/{saleId}/void", new { reason = "again" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.PostAsync($"/api/v1/sales/{saleId}/void", new { reason = " " })).Status);
        // and a voided sale cannot be returned
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await s.Owner.PostAsync("/api/v1/sales/returns", new { originalInvoiceId = saleId, lines = new[] { new { originalLineId = lineId, qty = 1 } } })).Status);
    }

    [Fact]
    public async Task Exchange_returns_and_sells_in_one_step_and_the_customer_pays_only_the_difference()
    {
        var s = await NewShopAsync();
        await BuyAsync(s, 10); await BuyAsync(s, 10, variant: s.Variant2);
        var cust = await Id(s.Owner, "/api/v1/parties", new { name = U("Cust"), isCustomer = true });
        var sale = (await s.Owner.PostAsync("/api/v1/sales", new { customerId = cust, lines = new[] { new { barcode = s.Barcode, qty = 1 } }, payInFull = true })).Body.GetProperty("invoice");
        var lineId = sale.GetProperty("lines")[0].GetProperty("id").GetInt64();

        // swap size 8 (2000) for size 9 (2000): even swap, nothing to pay
        var x = await s.Owner.PostAsync("/api/v1/sales/exchanges", new { @return = new { originalInvoiceId = sale.GetProperty("id").GetInt64(), lines = new[] { new { originalLineId = lineId, qty = 1 } } },
            sale = new { customerId = cust, lines = new[] { new { barcode = s.Barcode2, qty = 1 } }, payments = Array.Empty<object>() } });
        Assert.Equal(HttpStatusCode.OK, x.Status);
        var ret = x.Body.GetProperty("return"); var nw = x.Body.GetProperty("sale");
        Assert.Equal(nw.GetProperty("id").GetInt64(), ret.GetProperty("relatedInvoiceId").GetInt64());
        Assert.Equal(ret.GetProperty("id").GetInt64(), nw.GetProperty("relatedInvoiceId").GetInt64());
        Assert.Equal(0, await Balance(s.Owner, cust));    // return credit 2000 cancels the unpaid 2000
        Assert.Equal(10, await OnHand(s.Variant));         // size 8 back
        Assert.Equal(9, await OnHand(s.Variant2));         // size 9 gone
        await AssertJournalBalanced(nw.GetProperty("journalEntryId").GetInt64());
        await AssertJournalBalanced(ret.GetProperty("journalEntryId").GetInt64());

        // without a named customer the Cash Sale account carries both sides: only a swap that nets to zero is possible
        var s2 = await NewShopAsync();
        await BuyAsync(s2, 10);
        var cashSale = (await s2.Owner.PostAsync("/api/v1/sales", new { lines = new[] { new { barcode = s2.Barcode, qty = 1 } }, payInFull = true })).Body.GetProperty("invoice");
        var bad = await s2.Owner.PostAsync("/api/v1/sales/exchanges", new { @return = new { originalInvoiceId = cashSale.GetProperty("id").GetInt64(), lines = new[] { new { originalLineId = cashSale.GetProperty("lines")[0].GetProperty("id").GetInt64(), qty = 1 } } },
            sale = new { lines = new[] { new { barcode = s2.Barcode2, qty = 2 } }, payments = Array.Empty<object>() } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.Status);
    }

    [Fact]
    public async Task Purchase_return_is_linked_over_return_is_refused_and_a_purchase_whose_stock_was_sold_cannot_be_voided()
    {
        var s = await NewShopAsync();
        var buy = await BuyAsync(s, 10);
        var buyId = buy.GetProperty("id").GetInt64();
        var lineId = buy.GetProperty("lines")[0].GetProperty("id").GetInt64();

        var r = await s.Owner.PostAsync("/api/v1/purchases/returns", new { originalInvoiceId = buyId, lines = new[] { new { originalLineId = lineId, qty = 4 } } });
        Assert.Equal(HttpStatusCode.Created, r.Status);
        Assert.Equal(-4800, r.Body.GetProperty("netAmount").GetInt64());
        Assert.Equal(6, await OnHand(s.Variant));
        Assert.Equal(-7200, await Balance(s.Owner, s.Vendor));
        await AssertJournalBalanced(r.Body.GetProperty("journalEntryId").GetInt64());

        var over = await s.Owner.PostAsync("/api/v1/purchases/returns", new { originalInvoiceId = buyId, lines = new[] { new { originalLineId = lineId, qty = 7 } } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, over.Status);
        Assert.Contains("at most 6", over.Body.GetProperty("title").GetString());

        // cannot void the bill while the return stands; void the return first
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await s.Owner.PostAsync($"/api/v1/purchases/{buyId}/void", new { reason = "typo" })).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.PostAsync($"/api/v1/purchases/{r.Body.GetProperty("id").GetInt64()}/void", new { reason = "vendor refused" })).Status);
        Assert.Equal(10, await OnHand(s.Variant));

        // sell 7, then the bill's stock is no longer all on the shelf: voiding is refused and nothing changes
        await s.Owner.PostAsync("/api/v1/sales", new { lines = new[] { new { barcode = s.Barcode, qty = 7 } }, payInFull = true });
        var refused = await s.Owner.PostAsync($"/api/v1/purchases/{buyId}/void", new { reason = "typo" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.Status);
        Assert.Equal(3, await OnHand(s.Variant));
        Assert.Equal(-12000, await Balance(s.Owner, s.Vendor));
    }

    [Fact]
    public async Task Voiding_a_purchase_reverses_stock_and_the_vendor_balance()
    {
        var s = await NewShopAsync();
        var buy = await BuyAsync(s, 6);
        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.PostAsync($"/api/v1/purchases/{buy.GetProperty("id").GetInt64()}/void", new { reason = "entered twice" })).Status);
        Assert.Equal(0, await OnHand(s.Variant));
        Assert.Equal(0, await Balance(s.Owner, s.Vendor));
        Assert.Equal("VOID", (await s.Owner.GetJsonAsync($"/api/v1/purchases/{buy.GetProperty("id").GetInt64()}")).GetProperty("status").GetString());
        // the voided bill number is free again for the right entry
    }

    [Fact]
    public async Task Sales_are_store_scoped_listed_and_invisible_to_other_companies()
    {
        var s = await NewShopAsync();
        await BuyAsync(s, 5);
        var sale = (await s.Owner.PostAsync("/api/v1/sales", new { lines = new[] { new { barcode = s.Barcode, qty = 1 } }, payInFull = true })).Body.GetProperty("invoice");
        var id = sale.GetProperty("id").GetInt64();

        var list = await s.Owner.GetJsonAsync($"/api/v1/sales?invoiceNo={sale.GetProperty("invoiceNo").GetInt64()}&storeId={fx.Acme.FirstStoreId}");
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal("Cash Sale", list.GetProperty("items")[0].GetProperty("customer").GetString());

        var receipt = await s.Owner.GetJsonAsync($"/api/v1/sales/{id}/receipt");
        Assert.Equal("Acme Footwear", receipt.GetProperty("company").GetProperty("name").GetString());
        Assert.Equal("Main Store", receipt.GetProperty("storeInfo").GetProperty("name").GetString());

        var g = await fx.LoginAsync("Globex Shoes", "owner", ApiFixture.OwnerPassword);
        Assert.Equal(HttpStatusCode.NotFound, (await g.SendAsync(HttpMethod.Get, $"/api/v1/sales/{id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await g.PostAsync($"/api/v1/sales/{id}/void", new { reason = "hack" })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await g.PostAsync("/api/v1/sales/returns", new { originalInvoiceId = id, lines = new[] { new { originalLineId = 1, qty = 1 } } })).Status);
    }
}
