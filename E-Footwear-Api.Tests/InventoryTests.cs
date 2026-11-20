using System.Net;
using System.Text.Json;
using EFootwear.Api.Tests.Support;
using Xunit;

namespace EFootwear.Api.Tests;

[Collection("api")]
public sealed class InventoryTests(ApiFixture fx)
{
    private static string U(string p) => p + Guid.NewGuid().ToString("N")[..6];

    private sealed record Setup(Session Owner, long Store1, long Store2, long Variant, string Barcode);

    private static async Task<long> Id(Session s, string url, object body)
    {
        var (st, b) = await s.PostAsync(url, body);
        Assert.True(st == HttpStatusCode.Created, $"{url} -> {st} {b}");
        return b.GetProperty("id").GetInt64();
    }

    /// <summary>Two stores and one article with <paramref name="stock"/> pieces in store 1.</summary>
    private async Task<Setup> NewSetupAsync(int stock = 10)
    {
        var o = await fx.OwnerAsync();
        var dep = await Id(o, "/api/v1/masters/departments", new { name = U("D") });
        var cat = await Id(o, "/api/v1/masters/categories", new { name = U("C"), departmentId = dep });
        var vendor = await Id(o, "/api/v1/parties", new { name = U("V"), isVendor = true });
        var s2 = await Id(o, "/api/v1/stores", new { code = U("T").ToUpperInvariant(), name = "Second " + U("") });
        var (st, b) = await o.PostAsync("/api/v1/catalog/products", new { name = U("Shoe"), departmentId = dep, categoryId = cat, variants = new[] { new { sizeCode = "8", purchasePrice = 1000, salePrice = 1800 } } });
        Assert.Equal(HttpStatusCode.Created, st);
        var p = await o.GetJsonAsync($"/api/v1/catalog/products/{b.GetProperty("id").GetInt64()}");
        var v = p.GetProperty("variants")[0];
        var variant = v.GetProperty("id").GetInt64();
        if (stock > 0) Assert.Equal(HttpStatusCode.Created, (await o.PostAsync("/api/v1/purchases", new { vendorId = vendor, lines = new[] { new { variantId = variant, qty = stock, unitCost = 1000 } } })).Status);
        return new Setup(o, fx.Acme.FirstStoreId, s2, variant, v.GetProperty("barcode").GetString()!);
    }

    private Task<decimal> OnHand(long variant, long store)
        => fx.ScalarAsync<decimal>("select coalesce(sum(qty_on_hand), 0) from inv.stock_balances where company_id = @c and variant_id = @v and store_id = @s", ("c", fx.Acme.CompanyId), ("v", variant), ("s", store));

    private async Task<Session> UserAsync(Session owner, long store, params string[] perms)
    {
        var name = U("u");
        await Id(owner, "/api/v1/users", new { userName = name, fullName = name, role = "STORE_MANAGER", password = "Start-Pass-1", storeIds = new[] { store }, permissions = perms });
        var s = await fx.LoginAsync("Acme Footwear", name, "Start-Pass-1");
        await s.PostAsync("/api/v1/auth/change-password", new { currentPassword = "Start-Pass-1", newPassword = "Next-Pass-22" });
        return await fx.LoginAsync("Acme Footwear", name, "Next-Pass-22");
    }

    private async Task AssertBalanced(long entry)
        => Assert.Equal(0L, await fx.ScalarAsync<long>("select coalesce(sum(debit) - sum(credit), 0) from acct.journal_lines where company_id = @c and entry_id = @e", ("c", fx.Acme.CompanyId), ("e", entry)));

    private async Task<long> ShippedTransferAsync(Setup s, int send)
    {
        var t = await Id(s.Owner, "/api/v1/transfers", new { fromStoreId = s.Store1, toStoreId = s.Store2, note = "restock" });
        Assert.Equal(HttpStatusCode.OK, (await s.Owner.PostAsync($"/api/v1/transfers/{t}/scan-send", new { barcode = s.Barcode, qty = send })).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.PostAsync($"/api/v1/transfers/{t}/ship")).Status);
        return t;
    }

    [Fact]
    public async Task A_complete_transfer_moves_stock_through_transit_with_balanced_journals()
    {
        var s = await NewSetupAsync(10);
        var t = await Id(s.Owner, "/api/v1/transfers", new { fromStoreId = s.Store1, toStoreId = s.Store2 });

        var scanned = (await s.Owner.PostAsync($"/api/v1/transfers/{t}/scan-send", new { barcode = s.Barcode.ToLowerInvariant(), qty = 4 })).Body;
        Assert.Equal(4, scanned.GetProperty("lines")[0].GetProperty("qtyScanned").GetDecimal());
        Assert.Equal(10, await OnHand(s.Variant, s.Store1));   // nothing moves until shipping

        // a replayed scan id is counted once
        var uuid = Guid.NewGuid();
        await s.Owner.PostAsync($"/api/v1/transfers/{t}/scan-send", new { barcode = s.Barcode, scanUuid = uuid });
        var again = (await s.Owner.PostAsync($"/api/v1/transfers/{t}/scan-send", new { barcode = s.Barcode, scanUuid = uuid })).Body;
        Assert.Equal(5, again.GetProperty("lines")[0].GetProperty("qtyScanned").GetDecimal());

        // cannot pack more than the sender has
        var tooMany = await s.Owner.PostAsync($"/api/v1/transfers/{t}/scan-send", new { barcode = s.Barcode, qty = 6 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooMany.Status);
        Assert.Contains("insufficient stock", tooMany.Body.GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await s.Owner.PostAsync($"/api/v1/transfers/{t}/scan-send", new { barcode = "NOPE-123" })).Status);

        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.PostAsync($"/api/v1/transfers/{t}/ship")).Status);
        Assert.Equal(5, await OnHand(s.Variant, s.Store1));
        Assert.Equal(0, await OnHand(s.Variant, s.Store2));
        Assert.Equal("SHIPPED", (await s.Owner.GetJsonAsync($"/api/v1/transfers/{t}")).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await s.Owner.PostAsync($"/api/v1/transfers/{t}/scan-send", new { barcode = s.Barcode })).Status);   // shipped: no more packing
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await s.Owner.PostAsync($"/api/v1/transfers/{t}/ship")).Status);

        Assert.Equal(HttpStatusCode.OK, (await s.Owner.PostAsync($"/api/v1/transfers/{t}/scan-receive", new { barcode = s.Barcode, qty = 5 })).Status);
        var over = await s.Owner.PostAsync($"/api/v1/transfers/{t}/scan-receive", new { barcode = s.Barcode });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, over.Status);
        Assert.Contains("over-received", over.Body.GetProperty("title").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.PostAsync($"/api/v1/transfers/{t}/close")).Status);
        Assert.Equal(5, await OnHand(s.Variant, s.Store2));
        var done = await s.Owner.GetJsonAsync($"/api/v1/transfers/{t}");
        Assert.Equal("COMPLETED", done.GetProperty("status").GetString());
        Assert.Equal(0, done.GetProperty("discrepancies").GetArrayLength());
        // the stock in transit is back to nothing and every entry balances
        Assert.Equal(0L, await fx.ScalarAsync<long>("select coalesce(sum(debit) - sum(credit), 0) from acct.journal_lines l join acct.accounts a on a.company_id = l.company_id and a.id = l.account_id where l.company_id = @c and a.system_role = 'TRANSIT' and l.entry_id in (select id from acct.journal_entries where source_type = 'TRANSFER' and source_id = @t)", ("c", fx.Acme.CompanyId), ("t", t)));
        foreach (var e in await EntriesAsync("TRANSFER", t)) await AssertBalanced(e);
    }

    private Task<List<long>> EntriesAsync(string type, long id) => Task.Run(async () =>
    {
        var n = await fx.ScalarAsync<long>("select count(*) from acct.journal_entries where company_id = @c and source_type = @t and source_id = @i", ("c", fx.Acme.CompanyId), ("t", type), ("i", id));
        var list = new List<long>();
        for (var i = 0; i < n; i++) list.Add(await fx.ScalarAsync<long>("select id from acct.journal_entries where company_id = @c and source_type = @t and source_id = @i order by id offset @o limit 1", ("c", fx.Acme.CompanyId), ("t", type), ("i", id), ("o", i)));
        return list;
    });

    [Fact]
    public async Task Missing_pieces_become_loss_cases_that_are_found_or_charged_to_a_chosen_bearer()
    {
        var s = await NewSetupAsync(10);
        var t = await ShippedTransferAsync(s, 6);
        await s.Owner.PostAsync($"/api/v1/transfers/{t}/scan-receive", new { barcode = s.Barcode, qty = 4 });
        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.PostAsync($"/api/v1/transfers/{t}/close")).Status);
        Assert.Equal(4, await OnHand(s.Variant, s.Store2));
        var det = await s.Owner.GetJsonAsync($"/api/v1/transfers/{t}");
        Assert.Equal("COMPLETED_WITH_MISSING", det.GetProperty("status").GetString());
        Assert.Equal(2, det.GetProperty("discrepancies")[0].GetProperty("difference").GetDecimal());

        var cases = (await s.Owner.GetJsonAsync("/api/v1/transfers/loss-cases?status=OPEN")).EnumerateArray().Where(c => c.GetProperty("transferId").GetInt64() == t).ToList();
        Assert.Single(cases);
        var caseId = cases[0].GetProperty("id").GetInt64();
        Assert.Equal(2, cases[0].GetProperty("qty").GetDecimal());
        Assert.Equal(2000, cases[0].GetProperty("value").GetInt64());

        Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.PostAsync($"/api/v1/transfers/loss-cases/{caseId}/decide", new { decision = "CHARGE" })).Status);   // bearer missing
        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.PostAsync($"/api/v1/transfers/loss-cases/{caseId}/decide", new { decision = "FOUND" })).Status);
        Assert.Equal(6, await OnHand(s.Variant, s.Store2));   // the pieces turned up at the receiver
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await s.Owner.PostAsync($"/api/v1/transfers/loss-cases/{caseId}/decide", new { decision = "FOUND" })).Status);   // already decided

        // a second shipment, charged to the sender store
        var t2 = await ShippedTransferAsync(s, 3);
        await s.Owner.PostAsync($"/api/v1/transfers/{t2}/close");
        var c2 = (await s.Owner.GetJsonAsync("/api/v1/transfers/loss-cases?status=OPEN")).EnumerateArray().First(c => c.GetProperty("transferId").GetInt64() == t2).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.PostAsync($"/api/v1/transfers/loss-cases/{c2}/decide", new { decision = "CHARGE", bearer = "SENDER_STORE" })).Status);
        Assert.Equal("WRITTEN_OFF", (await s.Owner.GetJsonAsync("/api/v1/transfers/loss-cases?status=WRITTEN_OFF")).EnumerateArray().First(c => c.GetProperty("id").GetInt64() == c2).GetProperty("status").GetString());
        var loss = await fx.ScalarAsync<long>("select coalesce(sum(l.debit), 0) from acct.journal_lines l join acct.accounts a on a.company_id = l.company_id and a.id = l.account_id where l.company_id = @c and a.system_role = 'STOCK_LOSS' and a.store_id = @s", ("c", fx.Acme.CompanyId), ("s", s.Store1));
        Assert.True(loss >= 3000);   // 3 pieces at cost 1000 charged to the sending store's loss account
    }

    [Fact]
    public async Task Transfer_permissions_follow_the_store_only_the_sender_ships_and_only_the_receiver_receives()
    {
        var s = await NewSetupAsync(10);
        var sender = await UserAsync(s.Owner, s.Store1, "TRANSFER_CREATE", "TRANSFER_RECEIVE");
        var receiver = await UserAsync(s.Owner, s.Store2, "TRANSFER_CREATE", "TRANSFER_RECEIVE");
        var t = (await sender.PostAsync("/api/v1/transfers", new { fromStoreId = s.Store1, toStoreId = s.Store2 })).Body.GetProperty("id").GetInt64();

        // the receiving store's user cannot pack or ship the sender's draft... and cannot even see it as theirs to edit
        Assert.Equal(HttpStatusCode.Forbidden, (await receiver.PostAsync($"/api/v1/transfers/{t}/scan-send", new { barcode = s.Barcode })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await receiver.PostAsync($"/api/v1/transfers/{t}/ship")).Status);
        Assert.Equal(HttpStatusCode.OK, (await sender.PostAsync($"/api/v1/transfers/{t}/scan-send", new { barcode = s.Barcode, qty = 2 })).Status);
        var shipRes = await sender.PostAsync($"/api/v1/transfers/{t}/ship"); Assert.True(shipRes.Status == HttpStatusCode.NoContent, shipRes.Body.ToString());

        // the sender cannot receive their own shipment; the receiver can
        Assert.Equal(HttpStatusCode.Forbidden, (await sender.PostAsync($"/api/v1/transfers/{t}/scan-receive", new { barcode = s.Barcode })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await sender.PostAsync($"/api/v1/transfers/{t}/close")).Status);
        Assert.Equal(HttpStatusCode.OK, (await receiver.PostAsync($"/api/v1/transfers/{t}/scan-receive", new { barcode = s.Barcode, qty = 2 })).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await receiver.PostAsync($"/api/v1/transfers/{t}/close")).Status);

        // both stores see it, a third store does not
        var other = await Id(s.Owner, "/api/v1/stores", new { code = U("X").ToUpperInvariant(), name = "Third" });
        var third = await UserAsync(s.Owner, other, "TRANSFER_CREATE", "TRANSFER_RECEIVE");
        Assert.Equal(1, (await receiver.GetJsonAsync($"/api/v1/transfers?storeId={s.Store2}")).GetProperty("items").EnumerateArray().Count(x => x.GetProperty("id").GetInt64() == t));
        Assert.Equal(HttpStatusCode.NotFound, (await third.SendAsync(HttpMethod.Get, $"/api/v1/transfers/{t}")).Status);
        // loss decisions need their own right
        Assert.Equal(HttpStatusCode.Forbidden, (await receiver.PostAsync("/api/v1/transfers/loss-cases/1/decide", new { decision = "FOUND" })).Status);

        // an empty draft cannot ship; a draft can be cancelled, a shipped one cannot
        var empty = (await sender.PostAsync("/api/v1/transfers", new { fromStoreId = s.Store1, toStoreId = s.Store2 })).Body.GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await sender.PostAsync($"/api/v1/transfers/{empty}/ship")).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await sender.PostAsync($"/api/v1/transfers/{empty}/cancel")).Status);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await sender.PostAsync($"/api/v1/transfers/{t}/cancel")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await sender.PostAsync("/api/v1/transfers", new { fromStoreId = s.Store1, toStoreId = s.Store1 })).Status);
    }

    [Fact]
    public async Task Adjustments_move_stock_and_book_the_value_audits_post_only_differences()
    {
        var s = await NewSetupAsync(10);
        var adj = await s.Owner.PostAsync("/api/v1/inventory/adjustments", new { reason = "Damaged in store", lines = new[] { new { barcode = s.Barcode, qty = -3, reason = "water" } } });
        Assert.Equal(HttpStatusCode.Created, adj.Status);
        Assert.Equal(7, await OnHand(s.Variant, s.Store1));
        Assert.Equal(-3000, adj.Body.GetProperty("lines")[0].GetProperty("value").GetInt64());
        var entries = await EntriesAsync("ADJUSTMENT", adj.Body.GetProperty("id").GetInt64());
        Assert.Single(entries); await AssertBalanced(entries[0]);

        var gain = await s.Owner.PostAsync("/api/v1/inventory/adjustments", new { lines = new[] { new { barcode = s.Barcode, qty = 2, unitCost = 1000 } } });
        Assert.Equal(HttpStatusCode.Created, gain.Status);
        Assert.Equal(9, await OnHand(s.Variant, s.Store1));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await s.Owner.PostAsync("/api/v1/inventory/adjustments", new { lines = new[] { new { barcode = s.Barcode, qty = -50 } } })).Status);   // not enough stock
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.PostAsync("/api/v1/inventory/adjustments", new { lines = new[] { new { barcode = s.Barcode, qty = 0 } } })).Status);

        // audit: count 8 where the books say 9 -> one loss of 1; counting 9 later changes nothing
        var audit = await s.Owner.PostAsync("/api/v1/inventory/audits", new { counts = new[] { new { barcode = s.Barcode, counted = 5 }, new { barcode = s.Barcode, counted = 3 } } });   // two scanners add up to 8
        Assert.Equal(HttpStatusCode.OK, audit.Status);
        Assert.True(audit.Body.GetProperty("posted").GetBoolean());
        Assert.Equal(8, await OnHand(s.Variant, s.Store1));
        Assert.Equal(9, audit.Body.GetProperty("lines")[0].GetProperty("expectedQty").GetDecimal());
        Assert.Equal(-1, audit.Body.GetProperty("lines")[0].GetProperty("qty").GetDecimal());
        var match = await s.Owner.PostAsync("/api/v1/inventory/audits", new { counts = new[] { new { barcode = s.Barcode, counted = 8 } } });
        Assert.False(match.Body.GetProperty("posted").GetBoolean());
        Assert.Equal(8, await OnHand(s.Variant, s.Store1));

        // listing and the document view
        var list = await s.Owner.GetJsonAsync($"/api/v1/inventory/documents?storeId={s.Store1}&docType=ADJUSTMENT");
        Assert.True(list.GetProperty("total").GetInt32() >= 2);
    }

    [Fact]
    public async Task Claims_are_requested_by_the_store_approved_or_rejected_by_a_manager_and_approval_books_the_loss()
    {
        var s = await NewSetupAsync(10);
        var clerk = await UserAsync(s.Owner, s.Store1, "CLAIM_REQUEST");
        var req = await clerk.PostAsync("/api/v1/inventory/claims", new { barcode = s.Barcode, qty = 2, reason = "DAMAGED", note = "torn strap" });
        Assert.Equal(HttpStatusCode.Created, req.Status);
        var id = req.Body.GetProperty("id").GetInt64();
        Assert.Equal("REQUESTED", req.Body.GetProperty("status").GetString());
        Assert.Equal(10, await OnHand(s.Variant, s.Store1));   // a request moves nothing

        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.PostAsync($"/api/v1/inventory/claims/{id}/approve")).Status);   // the requester cannot approve
        Assert.Equal(HttpStatusCode.BadRequest, (await clerk.PostAsync("/api/v1/inventory/claims", new { barcode = s.Barcode, qty = 1, reason = "BORED" })).Status);

        var approved = await s.Owner.PostAsync($"/api/v1/inventory/claims/{id}/approve");
        Assert.Equal(HttpStatusCode.OK, approved.Status);
        Assert.Equal("APPROVED", approved.Body.GetProperty("status").GetString());
        Assert.Equal(1000m, approved.Body.GetProperty("unitCost").GetDecimal());
        Assert.Equal(8, await OnHand(s.Variant, s.Store1));
        await AssertBalanced(approved.Body.GetProperty("journalEntryId").GetInt64());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await s.Owner.PostAsync($"/api/v1/inventory/claims/{id}/approve")).Status);   // once only

        var r2 = (await clerk.PostAsync("/api/v1/inventory/claims", new { barcode = s.Barcode, qty = 1, reason = "LOST" })).Body.GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Owner.PostAsync($"/api/v1/inventory/claims/{r2}/reject", new { reason = " " })).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await s.Owner.PostAsync($"/api/v1/inventory/claims/{r2}/reject", new { reason = "found on shelf" })).Status);
        Assert.Equal("REJECTED", (await s.Owner.GetJsonAsync($"/api/v1/inventory/claims/{r2}")).GetProperty("status").GetString());
        Assert.Equal(8, await OnHand(s.Variant, s.Store1));

        // approval needs stock: 20 pieces cannot be written off from 8
        var big = (await clerk.PostAsync("/api/v1/inventory/claims", new { barcode = s.Barcode, qty = 20, reason = "LOST" })).Body.GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await s.Owner.PostAsync($"/api/v1/inventory/claims/{big}/approve")).Status);
        Assert.Equal(8, await OnHand(s.Variant, s.Store1));
    }
}
