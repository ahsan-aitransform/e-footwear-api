using EFootwear.Application.Abstractions;
using EFootwear.Application.Admin;
using EFootwear.Application.Catalog;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;

namespace EFootwear.Application.Trading;

public sealed record PurchaseLineBody(long? VariantId, string? Barcode, decimal Qty, long UnitCost, long? SalePrice, decimal? SaleDiscountPct, bool UpdatesSalePrice);

public sealed record PurchaseBody(long? StoreId, long VendorId, string? VendorBillNo, DateOnly? BillDate, DateOnly? ReceivedOn, DateOnly? DueDate, long Discount, long Tax,
                                  List<PurchaseLineBody>? Lines);

public sealed record PurchaseReturnLine(long OriginalLineId, decimal Qty);
public sealed record PurchaseReturnBody(long OriginalInvoiceId, List<PurchaseReturnLine>? Lines, string? Reason);

/// <summary>
/// Purchasing. A bill adds stock at cost, owes the vendor and (optionally) updates the sale price. The bill's discount and tax are spread over
/// the lines so the stock subledger and the journal (which books the net bill to Stock) agree. Returns must point at the bill lines they undo.
/// </summary>
public sealed class PurchaseService(IUnitOfWorkFactory uows, PriceRefresher prices)
{
    public async Task<Row> CreateAsync(UserContext u, PurchaseBody b, CancellationToken ct)
    {
        u.Require(P.PurchaseCreate);
        var store = b.StoreId ?? u.StoreId;
        u.RequireStore(store);
        if (store <= 0) throw new ValidationException("storeId", "Choose a selling store, not head office.");
        var lines = b.Lines ?? [];
        new Check().Require(lines.Count > 0, "lines", "Add at least one item.").Require(b.Discount >= 0, "discount", "Discount cannot be negative.")
            .Require(b.Tax >= 0, "tax", "Tax cannot be negative.").MaxLength(b.VendorBillNo, 60, "vendorBillNo")
            .Require(b.DueDate is null || b.DueDate >= (b.BillDate ?? DateOnly.FromDateTime(DateTime.UtcNow)), "dueDate", "The due date cannot be before the bill date.").ThrowIfAny();

        await using var uow = await uows.BeginAsync(u, ct);
        var vendor = await uow.Purchases.VendorAsync(b.VendorId, ct) ?? throw new ValidationException("vendorId", "Vendor not found.");
        if (!vendor.Get<bool>("isVendor") || vendor.Get<string>("status") != "ACTIVE") throw new ValidationException("vendorId", "This party is not an active vendor.");
        var company = await uow.Stores.GetCompanyAsync(u.CompanyId, ct);
        var mode = company?.Get<string>("roundingMode") ?? "FLOOR";

        var resolved = new List<(Row V, PurchaseLineBody L, long Amount)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            new Check().Require(l.Qty > 0, $"lines[{i}].qty", "Quantity must be above zero.").Require(l.UnitCost >= 0, $"lines[{i}].unitCost", "Cost cannot be negative.")
                .Require(l.SalePrice is null or >= 0, $"lines[{i}].salePrice", "Price cannot be negative.").ThrowIfAny();
            var vid = l.VariantId;
            if (vid is null)
            {
                if (string.IsNullOrWhiteSpace(l.Barcode)) throw new ValidationException($"lines[{i}]", "Scan a barcode or choose an item.");
                var hit = await uow.Catalog.LookupAsync(l.Barcode.Trim(), store, ct) ?? throw new NotFoundException("Barcode", l.Barcode);
                if (hit.Get<string>("kind") != "VARIANT") throw new ValidationException($"lines[{i}].barcode", "A deal barcode cannot be purchased.");
                vid = hit.Get<long>("variantId");
            }
            var v = await uow.Purchases.VariantAsync(vid.Value, ct) ?? throw new NotFoundException("Item", vid);
            resolved.Add((v, l, Money.Round(l.Qty * l.UnitCost, mode)));
        }

        var gross = resolved.Sum(r => r.Amount);
        if (b.Discount > gross) throw new ValidationException("discount", "The discount is more than the bill.");
        var net = gross - b.Discount + b.Tax;
        var billDate = b.BillDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var no = await uow.Purchases.NextNumberAsync(u.CompanyId, store, "PURCHASE", ct);
        var id = await uow.Purchases.InsertHeaderAsync(new PurchaseHeaderInsert(u.CompanyId, store, "PURCHASE", no, StoreService.Blank(b.VendorBillNo), b.VendorId, billDate, b.ReceivedOn ?? billDate, b.DueDate,
            gross, b.Discount, b.Tax, net), ct);

        // What each piece really costs: its price scaled by net/gross, so stock value equals the booked bill.
        var factor = gross > 0 ? (decimal)net / gross : 1m;
        for (var i = 0; i < resolved.Count; i++)
        {
            var (v, l, amount) = resolved[i];
            var vid = v.Get<long>("variantId");
            var salePrice = l.SalePrice ?? v.Get<long>("salePrice");
            await uow.Purchases.InsertLineAsync(new PurchaseLineInsert(u.CompanyId, id, i + 1, vid, l.Qty, l.UnitCost, salePrice, l.SaleDiscountPct, amount, l.UpdatesSalePrice, null), ct);
            await uow.StockMoves.PostMoveAsync(u.CompanyId, store, vid, l.Qty, "PURCHASE", id, Math.Round(l.UnitCost * factor, 4), billDate, ct);

            // last purchase price always follows the bill; the sale price only when the bill says so
            var oldPurchase = v.Get<long>("purchasePrice"); var oldSale = v.Get<long>("salePrice");
            var newSale = l.UpdatesSalePrice ? salePrice : oldSale;
            if (oldPurchase != l.UnitCost || newSale != oldSale)
            {
                await uow.Catalog.UpsertPricesAsync(u.CompanyId, vid, l.UnitCost, newSale, ct);
                if (oldPurchase != l.UnitCost) await uow.Catalog.InsertPriceEventAsync(u.CompanyId, vid, 0, "PURCHASE", oldPurchase, l.UnitCost, null, $"Purchase {no}", u.UserId, ct);
                if (newSale != oldSale) await uow.Catalog.InsertPriceEventAsync(u.CompanyId, vid, 0, "SALE", oldSale, newSale, null, $"Purchase {no}", u.UserId, ct);
            }
        }
        await uow.CommitAsync(ct);
        prices.MarkDirty(u.CompanyId);
        await using var read = await uows.BeginAsync(u, ct);
        return await DetailAsync(read, id, ct);
    }

    public async Task<Row> ReturnAsync(UserContext u, PurchaseReturnBody b, CancellationToken ct)
    {
        u.Require(P.PurchaseCreate);
        var wanted = b.Lines ?? [];
        new Check().Require(wanted.Count > 0, "lines", "Choose at least one item to return.").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        var original = await uow.Purchases.InvoiceAsync(b.OriginalInvoiceId, ct) ?? throw new NotFoundException("Purchase", b.OriginalInvoiceId);
        var store = original.Get<long>("storeId");
        u.RequireStore(store);
        if (original.Get<string>("docType") != "PURCHASE" || original.Get<string>("status") != "POSTED") throw new BusinessRuleException("Only a posted purchase can be returned.", "not_returnable");
        var company = await uow.Stores.GetCompanyAsync(u.CompanyId, ct);
        var mode = company?.Get<string>("roundingMode") ?? "FLOOR";

        var parts = new List<(Row Line, decimal Qty, long Amount)>();
        foreach (var w in wanted)
        {
            var l = await uow.Purchases.LineAsync(w.OriginalLineId, ct);
            if (l is null || l.Get<long>("invoiceId") != b.OriginalInvoiceId) throw new ValidationException("lines", $"Line {w.OriginalLineId} is not on this purchase.");
            var left = l.Get<decimal>("qty") - l.Get<decimal>("returnedQty");
            if (w.Qty <= 0 || w.Qty > left) throw new BusinessRuleException($"You can return at most {left:0.###} of that item (bought {l.Get<decimal>("qty"):0.###}, already returned {l.Get<decimal>("returnedQty"):0.###}).", "over_return");
            parts.Add((l, w.Qty, Money.Round(w.Qty * l.Get<long>("unitCost"), mode)));
        }

        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var gross = -parts.Sum(p => p.Amount);
        var no = await uow.Purchases.NextNumberAsync(u.CompanyId, store, "PURCHASE-RETURN", ct);
        var id = await uow.Purchases.InsertHeaderAsync(new PurchaseHeaderInsert(u.CompanyId, store, "RETURN", no, null, original.Get<long>("vendorPartyId"), date, date, null, gross, 0, 0, gross), ct);
        for (var i = 0; i < parts.Count; i++)
        {
            var (l, qty, amount) = parts[i];
            await uow.Purchases.InsertLineAsync(new PurchaseLineInsert(u.CompanyId, id, i + 1, l.Get<long>("variantId"), -qty, l.Get<long>("unitCost"), l.Get<long>("salePrice"), l.Get<decimal?>("saleDiscountPct"), -amount, false, l.Get<long>("id")), ct);
            await uow.StockMoves.PostMoveAsync(u.CompanyId, store, l.Get<long>("variantId"), -qty, "PURCHASE_RETURN", id, 0, date, ct);
        }
        await uow.CommitAsync(ct);
        await using var read = await uows.BeginAsync(u, ct);
        return await DetailAsync(read, id, ct);
    }

    /// <summary>Cancels a posted bill or return. The journal is reversed by the database; stock is undone here (and is refused if the stock was already sold).</summary>
    public async Task VoidAsync(UserContext u, long id, VoidBody b, CancellationToken ct)
    {
        u.Require(P.VoucherDelete);
        new Check().NotBlank(b.Reason, "reason", "Reason").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        var h = await uow.Purchases.InvoiceAsync(id, ct) ?? throw new NotFoundException("Purchase", id);
        var store = h.Get<long>("storeId");
        u.RequireStore(store);
        if (h.Get<string>("status") != "POSTED") throw new BusinessRuleException("This document is already void.", "already_void");
        var isPurchase = h.Get<string>("docType") == "PURCHASE";
        if (isPurchase && await uow.Purchases.LiveReturnCountAsync(id, ct) > 0) throw new BusinessRuleException("This purchase has returns against it. Void the returns first.", "has_returns");

        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var l in await uow.Purchases.InvoiceLinesAsync(id, ct))
        {
            // a bill's stock goes back out, a return's stock comes back in. The stock ledger has no "void" type for purchases, so the opposite
            // purchase document type is used and the document id points at the voided invoice.
            await uow.StockMoves.PostMoveAsync(u.CompanyId, store, l.Get<long>("variantId"), -l.Get<decimal>("qty"), isPurchase ? "PURCHASE_RETURN" : "PURCHASE", id,
                l.Get<long>("unitCost"), date, ct);
        }
        await uow.Purchases.VoidAsync(id, b.Reason!.Trim(), u.UserId, ct);
        await uow.CommitAsync(ct);
    }

    public async Task<Row> GetAsync(UserContext u, long id, CancellationToken ct)
    {
        u.RequireAny(P.PurchaseCreate, P.PurchasePay, P.ReportView, P.VoucherPost);
        await using var uow = await uows.BeginAsync(u, ct);
        return await DetailAsync(uow, id, ct);
    }

    public async Task<Page<Row>> ListAsync(UserContext u, PurchaseFilter f, PageQuery q, CancellationToken ct)
    {
        u.RequireAny(P.PurchaseCreate, P.PurchasePay, P.ReportView, P.VoucherPost);
        if (f.StoreId is { } s) u.RequireStore(s);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Purchases.InvoicesAsync(f with { Search = q.Search }, q, ct);
    }

    private static async Task<Row> DetailAsync(IUnitOfWork uow, long id, CancellationToken ct)
    {
        var h = await uow.Purchases.InvoiceAsync(id, ct) ?? throw new NotFoundException("Purchase", id);
        h["lines"] = await uow.Purchases.InvoiceLinesAsync(id, ct);
        return h;
    }
}
