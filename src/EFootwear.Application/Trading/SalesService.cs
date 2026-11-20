using EFootwear.Application.Abstractions;
using EFootwear.Domain.Common;
using EFootwear.Domain.Sales;
using EFootwear.Domain.Security;

namespace EFootwear.Application.Trading;

public sealed record SaleLineBody(long? VariantId, string? Barcode, decimal Qty, long? UnitPrice, long? SalesmanId);
public sealed record PaymentBody(string? Method, long Amount, string? Reference);

public sealed record SaleBody(long? StoreId, long? CustomerId, long? SalesmanId, string? WalkinName, string? WalkinMobile, Guid? SaleUuid, DateTimeOffset? OccurredAt,
                              List<SaleLineBody>? Lines, long BillDiscount, List<PaymentBody>? Payments, bool PayInFull);

public sealed record ReturnLineBody(long OriginalLineId, decimal Qty);
public sealed record ReturnBody(long OriginalInvoiceId, List<ReturnLineBody>? Lines, string? Reason);
public sealed record ExchangeBody(ReturnBody? Return, SaleBody? Sale);
public sealed record VoidBody(string? Reason);

/// <summary>Outcome of posting a sale: the saved invoice and whether this was a retry of one already stored (same SaleUuid).</summary>
public sealed record PostedInvoice(Row Invoice, bool Duplicate);

/// <summary>
/// Sales, returns, exchanges and voids. The server prices every line itself (promotion engine → store price → list price); the client may only
/// ask for a manual price, which needs the price/discount permission. Stock moves, journals, commissions and report roll-ups are done by the
/// database in the same transaction; this service builds a consistent invoice and lets the database refuse anything that is not.
/// </summary>
public sealed class SalesService(IUnitOfWorkFactory uows)
{
    private static readonly string[] Methods = ["CASH", "CARD", "BANK_TRANSFER", "CHEQUE", "ONLINE"];

    // ------------------------------------------------------------------------------------------ sale

    public async Task<PostedInvoice> CreateSaleAsync(UserContext u, SaleBody b, CancellationToken ct)
    {
        u.Require(P.SaleCreate);
        var store = b.StoreId ?? u.StoreId;
        u.RequireStore(store);
        await using var uow = await uows.BeginAsync(u, ct);
        if (b.SaleUuid is { } uuid && await uow.Sales.FindBySaleUuidAsync(uuid, ct) is { } existing)
            return new PostedInvoice(await DetailAsync(uow, existing.Get<long>("id"), ct), true);

        var ctx = await ContextAsync(u, uow, store, ct);
        var id = await PostSaleAsync(u, uow, ctx, b, store, null, ct);
        await uow.CommitAsync(ct);   // balanced journal, stock rules, return limits are checked here
        await using var read = await uows.BeginAsync(u, ct);
        return new PostedInvoice(await DetailAsync(read, id, ct), false);
    }

    private sealed record BizContext(string RoundingMode, DateOnly Today, long ChannelId, long? WalkinId);

    private static async Task<BizContext> ContextAsync(UserContext u, IUnitOfWork uow, long store, CancellationToken ct)
    {
        if (store <= 0) throw new ValidationException("storeId", "Choose a selling store, not head office.");
        var company = await uow.Stores.GetCompanyAsync(u.CompanyId, ct) ?? throw new NotFoundException("Company");
        var tz = TimeZoneInfo.TryFindSystemTimeZoneById(company.Get<string>("timezone") ?? "UTC", out var z) ? z : TimeZoneInfo.Utc;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime);
        return new BizContext(company.Get<string>("roundingMode") ?? "FLOOR", today, await uow.Stock.ChannelIdAsync("POS", ct), await uow.Sales.WalkinPartyAsync(ct));
    }

    private sealed record ResolvedLine(Row Snap, decimal Qty, long ListPrice, long UnitPrice, string Source, long? RuleId, long? ConflictGroupId, long? BundleId, long? ApprovedBy, long? SalesmanId);

    /// <summary>Builds, prices, stocks and stores one SALE invoice inside the caller's transaction. Returns the new invoice id.</summary>
    private async Task<long> PostSaleAsync(UserContext u, IUnitOfWork uow, BizContext biz, SaleBody b, long store, long? returnCredit, CancellationToken ct)
    {
        var lines = b.Lines ?? [];
        new Check().Require(lines.Count > 0, "lines", "Add at least one item.").Require(b.BillDiscount >= 0, "billDiscount", "Discount cannot be negative.").ThrowIfAny();
        if (b.BillDiscount > 0) u.Require(P.DiscountManual);

        var customerId = await ResolveCustomerAsync(uow, b.CustomerId, biz, ct);
        var salesmanId = await ResolveSalesmanAsync(uow, b.SalesmanId, ct);
        var resolved = await ResolveLinesAsync(u, uow, lines, store, salesmanId, ct);
        await CheckStockAsync(uow, store, resolved, ct);

        var totals = InvoiceCalculator.Sale(resolved.Select(r => new CalcLine(r.Qty, r.ListPrice, r.UnitPrice, r.Snap.Get<decimal>("taxRate"))).ToList(), b.BillDiscount, biz.RoundingMode);
        var (paid, change, credit, payments) = await PaymentsAsync(uow, b, totals.Net, ct);
        if (credit > 0)
        {
            var isWalkin = customerId == biz.WalkinId;
            if (isWalkin && (returnCredit is null || credit > returnCredit))
                throw new BusinessRuleException("A sale that is not paid in full needs a named customer, not Cash Sale.", "customer_required");
        }

        var occurred = b.OccurredAt ?? DateTimeOffset.UtcNow;
        var saleDate = b.OccurredAt is null ? biz.Today : DateOnly.FromDateTime(occurred.UtcDateTime.AddHours(await OffsetHoursAsync(uow, u, ct)));
        var invoiceNo = await uow.Sales.NextNumberAsync(u.CompanyId, store, "SALE", null, ct);
        var daily = (int)await uow.Sales.NextNumberAsync(u.CompanyId, store, "SALE-DAILY", saleDate, ct);
        var id = await uow.Sales.InsertHeaderAsync(new InvoiceHeaderInsert(u.CompanyId, store, biz.ChannelId, "SALE", invoiceNo, saleDate, daily, null, b.SaleUuid, occurred, null,
            customerId, salesmanId, StoreService_Blank(b.WalkinName), StoreService_Blank(b.WalkinMobile), null), ct);

        long cost = 0;
        for (var i = 0; i < resolved.Count; i++)
        {
            var r = resolved[i]; var t = totals.Lines[i];
            var unitCost = await uow.StockMoves.PostMoveAsync(u.CompanyId, store, r.Snap.Get<long>("variantId"), -r.Qty, "SALE", id, 0, saleDate, ct);
            var lineCost = Money.Round(unitCost * r.Qty, biz.RoundingMode);
            cost += lineCost;
            var source = r.Source == "NONE" && t.BillDiscountShare > 0 ? "MANUAL" : r.Source;
            await uow.Sales.InsertLineAsync(new InvoiceLineInsert(u.CompanyId, id, i + 1, store, biz.ChannelId, saleDate, "SALE", r.Snap.Get<long>("variantId"), r.Qty, r.ListPrice, r.UnitPrice,
                source, r.RuleId, r.ConflictGroupId, r.BundleId, source == "MANUAL" ? r.ApprovedBy ?? (u.Has(P.DiscountApprove) ? u.UserId : null) : null, t.DiscountAmount,
                r.Snap.Get<decimal>("taxRate"), t.Taxable, t.Tax, t.Net, unitCost, lineCost, r.SalesmanId, null, null), ct);
        }
        foreach (var p in payments) await uow.Sales.InsertPaymentAsync(p with { InvoiceId = id, StoreId = store, SaleDate = saleDate, CompanyId = u.CompanyId }, ct);
        await uow.Sales.UpdateTotalsAsync(id, new InvoiceTotalsUpdate(totals.Gross, totals.LineDiscount, totals.BillDiscount, totals.Taxable, totals.Tax, totals.Net, paid, change, credit, cost), ct);
        return id;
    }

    private static async Task<int> OffsetHoursAsync(IUnitOfWork uow, UserContext u, CancellationToken ct)
    {
        var tzId = (await uow.Stores.GetCompanyAsync(u.CompanyId, ct))?.Get<string>("timezone") ?? "UTC";
        return TimeZoneInfo.TryFindSystemTimeZoneById(tzId, out var z) ? (int)z.BaseUtcOffset.TotalHours : 0;
    }

    private static string? StoreService_Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static async Task<long?> ResolveCustomerAsync(IUnitOfWork uow, long? customerId, BizContext biz, CancellationToken ct)
    {
        if (customerId is null) return biz.WalkinId;
        var p = await uow.Sales.PartyBriefAsync(customerId.Value, ct) ?? throw new ValidationException("customerId", "Customer not found.");
        if (!p.Get<bool>("isCustomer") || p.Get<string>("status") != "ACTIVE") throw new ValidationException("customerId", "This party is not an active customer.");
        return customerId;
    }

    private static async Task<long?> ResolveSalesmanAsync(IUnitOfWork uow, long? salesmanId, CancellationToken ct)
    {
        if (salesmanId is null) return null;
        var p = await uow.Sales.PartyBriefAsync(salesmanId.Value, ct) ?? throw new ValidationException("salesmanId", "Salesman not found.");
        if (!p.Get<bool>("isStaff")) throw new ValidationException("salesmanId", "The salesman must be an employee.");
        return salesmanId;
    }

    private static async Task<List<ResolvedLine>> ResolveLinesAsync(UserContext u, IUnitOfWork uow, List<SaleLineBody> lines, long store, long? headerSalesman, CancellationToken ct)
    {
        var result = new List<ResolvedLine>();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (l.Qty <= 0) throw new ValidationException($"lines[{i}].qty", "Quantity must be above zero.");
            var salesman = l.SalesmanId is null ? headerSalesman : await ResolveSalesmanAsync(uow, l.SalesmanId, ct);

            if (l.VariantId is null && string.IsNullOrWhiteSpace(l.Barcode)) throw new ValidationException($"lines[{i}]", "Scan a barcode or choose an item.");
            var variantId = l.VariantId;
            if (variantId is null)
            {
                var hit = await uow.Catalog.LookupAsync(l.Barcode!.Trim(), store, ct) ?? throw new NotFoundException("Barcode", l.Barcode);
                if (hit.Get<string>("kind") == "BUNDLE")
                {
                    if (l.UnitPrice is not null) throw new ValidationException($"lines[{i}].unitPrice", "A deal has a fixed price.");
                    var bundleId = hit.Get<long>("bundleId");
                    foreach (var item in (List<Row>)hit["items"]!)
                    {
                        var snapB = await uow.Sales.VariantSnapshotAsync(item.Get<long>("variantId"), store, ct) ?? throw new NotFoundException("Item");
                        EnsureSellable(snapB, i);
                        result.Add(new ResolvedLine(snapB, item.Get<decimal>("qty") * l.Qty, snapB.Get<long>("listPrice"), item.Get<long>("netUnitPrice"), "BUNDLE", null, null, bundleId, null, salesman));
                    }
                    continue;
                }
                variantId = hit.Get<long>("variantId");
            }

            var snap = await uow.Sales.VariantSnapshotAsync(variantId.Value, store, ct) ?? throw new NotFoundException("Item", variantId);
            EnsureSellable(snap, i);
            var list = snap.Get<long>("listPrice");
            var conflict = snap.Get<bool>("isConflict");
            var eff = snap.Get<long?>("effPrice");
            var serverPrice = !conflict && eff is { } e && e < list ? e : list;
            string source = serverPrice < list ? "RULE" : "NONE";
            long? rule = source == "RULE" ? snap.Get<long?>("ruleId") : null;
            long? approved = null; var unit = serverPrice;
            if (l.UnitPrice is { } manual && manual != serverPrice)
            {
                if (manual < 0) throw new ValidationException($"lines[{i}].unitPrice", "Price cannot be negative.");
                if (manual < serverPrice) { u.Require(P.DiscountManual); approved = u.Has(P.DiscountApprove) ? u.UserId : null; }
                else u.Require(P.PriceChange);
                unit = manual; source = "MANUAL"; rule = null;
            }
            result.Add(new ResolvedLine(snap, l.Qty, list, unit, source, rule, conflict ? snap.Get<long?>("conflictGroupId") : null, null, approved, salesman));
        }
        return result;
    }

    private static void EnsureSellable(Row snap, int i)
    {
        if (snap.Get<string>("variantStatus") != "ACTIVE" || snap.Get<string>("productStatus") != "ACTIVE")
            throw new BusinessRuleException($"{snap.Get<string>("productName")} ({snap.Get<string>("sizeCode")}) is inactive and cannot be sold.", "inactive_item");
    }

    private static async Task CheckStockAsync(IUnitOfWork uow, long store, List<ResolvedLine> lines, CancellationToken ct)
    {
        var need = lines.GroupBy(l => l.Snap.Get<long>("variantId")).ToDictionary(g => g.Key, g => g.Sum(l => l.Qty));
        var have = await uow.StockMoves.OnHandAsync(store, need.Keys, ct);
        var short1 = need.Where(n => have.GetValueOrDefault(n.Key) < n.Value).ToList();
        if (short1.Count == 0) return;
        var msgs = short1.Select(s =>
        {
            var l = lines.First(x => x.Snap.Get<long>("variantId") == s.Key).Snap;
            return $"{l.Get<string>("productName")} size {l.Get<string>("sizeCode")}: have {have.GetValueOrDefault(s.Key):0.###}, need {s.Value:0.###}";
        });
        throw new BusinessRuleException("Not enough stock. " + string.Join("; ", msgs), "insufficient_stock");
    }

    private async Task<(long Paid, long Change, long Credit, List<PaymentInsert> Rows)> PaymentsAsync(IUnitOfWork uow, SaleBody b, long net, CancellationToken ct)
    {
        var pays = b.PayInFull && (b.Payments is null || b.Payments.Count == 0) ? [new PaymentBody("CASH", net, null)] : b.Payments ?? [];
        var rows = new List<PaymentInsert>();
        foreach (var p in pays)
        {
            var m = (p.Method ?? "").Trim().ToUpperInvariant();
            if (!Methods.Contains(m)) throw new ValidationException("payments", $"Unknown payment method '{p.Method}'.");
            if (p.Amount <= 0) throw new ValidationException("payments", "A payment must be above zero.");
            var account = await uow.Sales.RoleAccountAsync(m == "CASH" ? "CASH" : "BANK", ct);
            rows.Add(new PaymentInsert(0, 0, 0, default, m, account, p.Amount, StoreService_Blank(p.Reference)));
        }
        var paid = rows.Sum(r => r.Amount);
        var cash = rows.Where(r => r.Method == "CASH").Sum(r => r.Amount);
        var change = Math.Max(0, paid - net);
        if (change > cash) throw new ValidationException("payments", "Only cash can be given back as change; the card/bank amount is more than the bill.");
        return (paid, change, Math.Max(0, net - (paid - change)), rows);
    }

    // ------------------------------------------------------------------------------------------ return

    public async Task<PostedInvoice> CreateReturnAsync(UserContext u, ReturnBody b, CancellationToken ct)
    {
        u.Require(P.SaleReturn);
        await using var uow = await uows.BeginAsync(u, ct);
        var original = await uow.Sales.InvoiceAsync(b.OriginalInvoiceId, ct) ?? throw new NotFoundException("Invoice", b.OriginalInvoiceId);
        var store = original.Get<long>("storeId");
        u.RequireStore(store);
        var ctx = await ContextAsync(u, uow, store, ct);
        var id = await PostReturnAsync(u, uow, ctx, original, b, original.Get<long?>("customerPartyId") is { } c && c == ctx.WalkinId ? null : original.Get<long?>("customerPartyId"), ct);
        await uow.CommitAsync(ct);
        await using var read = await uows.BeginAsync(u, ct);
        return new PostedInvoice(await DetailAsync(read, id, ct), false);
    }

    private static async Task<long> PostReturnAsync(UserContext u, IUnitOfWork uow, BizContext biz, Row original, ReturnBody b, long? customerId, CancellationToken ct)
    {
        if (original.Get<string>("docType") != "SALE" || original.Get<string>("status") != "POSTED") throw new BusinessRuleException("Only a posted sale can be returned.", "not_returnable");
        var wanted = b.Lines ?? [];
        new Check().Require(wanted.Count > 0, "lines", "Choose at least one item to return.").ThrowIfAny();
        var store = original.Get<long>("storeId");
        var origLines = (await uow.Sales.ReturnableLinesAsync(original.Get<long>("id"), ct)).ToDictionary(r => r.Get<long>("id"));

        var parts = new List<(Row Orig, decimal Qty, CalcLineResult Share)>();
        foreach (var w in wanted)
        {
            if (!origLines.TryGetValue(w.OriginalLineId, out var o)) throw new ValidationException("lines", $"Line {w.OriginalLineId} is not on this invoice.");
            var left = o.Get<decimal>("qty") - o.Get<decimal>("returnedQty");
            if (w.Qty <= 0 || w.Qty > left) throw new BusinessRuleException($"You can return at most {left:0.###} of that item (sold {o.Get<decimal>("qty"):0.###}, already returned {o.Get<decimal>("returnedQty"):0.###}).", "over_return");
            // The share is taken of the quantity still returnable, in proportion to what was originally sold.
            var share = InvoiceCalculator.ReturnShare(w.Qty, o.Get<decimal>("qty"), (long)o.Get<decimal>("grossValue"), o.Get<long>("discountAmount"), o.Get<long>("taxableAmount"), o.Get<long>("taxAmount"));
            parts.Add((o, w.Qty, share));
        }

        var saleDate = biz.Today;
        var no = await uow.Sales.NextNumberAsync(u.CompanyId, store, "RETURN", null, ct);
        var id = await uow.Sales.InsertHeaderAsync(new InvoiceHeaderInsert(u.CompanyId, store, biz.ChannelId, "RETURN", no, saleDate, null, null, null, DateTimeOffset.UtcNow,
            original.Get<long>("id"), customerId, original.Get<long?>("salesmanPartyId"), null, null, null), ct);

        long cost = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            var (o, qty, s) = parts[i];
            var unitCost = o.Get<decimal>("unitCost");
            await uow.StockMoves.PostMoveAsync(u.CompanyId, store, o.Get<long>("variantId"), qty, "SALE_RETURN", id, unitCost, saleDate, ct);
            var lineCost = Money.Round(unitCost * qty, biz.RoundingMode);
            cost += lineCost;
            await uow.Sales.InsertLineAsync(new InvoiceLineInsert(u.CompanyId, id, i + 1, store, biz.ChannelId, saleDate, "RETURN", o.Get<long>("variantId"), qty, o.Get<long>("listPrice"), o.Get<long>("unitPrice"),
                o.Get<string>("discountSource")!, o.Get<long?>("ruleId"), null, o.Get<long?>("bundleId"), null, s.DiscountAmount, o.Get<decimal>("taxRate"), s.Taxable, s.Tax, s.Net, unitCost, -lineCost,
                o.Get<long?>("salesmanPartyId"), o.Get<long>("id"), o.Get<DateOnly>("saleDate")), ct);
        }
        var gross = parts.Sum(p => p.Share.Gross); var disc = parts.Sum(p => p.Share.LineDiscount); var taxable = parts.Sum(p => p.Share.Taxable);
        var tax = parts.Sum(p => p.Share.Tax); var net = parts.Sum(p => p.Share.Net);
        await uow.Sales.UpdateTotalsAsync(id, new InvoiceTotalsUpdate(gross, disc, 0, taxable, tax, net, 0, 0, 0, -cost), ct);
        return id;
    }

    // ------------------------------------------------------------------------------------------ exchange

    /// <summary>
    /// A return and a new sale for the same customer in one transaction, linked to each other. The returned value is credited to the customer
    /// account and the new sale's unpaid part is debited to it, so the two net off; the customer pays only the difference. Without a named
    /// customer the Cash Sale account carries both sides (so only an exact swap or a cash top-up is possible).
    /// </summary>
    public async Task<Row> ExchangeAsync(UserContext u, ExchangeBody b, CancellationToken ct)
    {
        u.RequireAny(P.SaleReturn);
        u.Require(P.SaleCreate);
        if (b.Return is null || b.Sale is null) throw new ValidationException("exchange", "An exchange needs both returned items and new items.");
        await using var uow = await uows.BeginAsync(u, ct);
        var original = await uow.Sales.InvoiceAsync(b.Return.OriginalInvoiceId, ct) ?? throw new NotFoundException("Invoice", b.Return.OriginalInvoiceId);
        var store = original.Get<long>("storeId");
        u.RequireStore(store);
        if (b.Sale.StoreId is { } s2 && s2 != store) throw new ValidationException("sale.storeId", "An exchange happens in one store.");
        var ctx = await ContextAsync(u, uow, store, ct);

        if (b.Sale.SaleUuid is { } uuid && await uow.Sales.FindBySaleUuidAsync(uuid, ct) is { } existing)
            return new Row { ["duplicate"] = true, ["sale"] = await DetailAsync(uow, existing.Get<long>("id"), ct) };

        var customer = b.Sale.CustomerId ?? original.Get<long?>("customerPartyId") ?? ctx.WalkinId;
        var retId = await PostReturnAsync(u, uow, ctx, original, b.Return, customer, ct);
        var retNet = -(await uow.Sales.InvoiceAsync(retId, ct))!.Get<long>("netAmount");
        var saleId = await PostSaleAsync(u, uow, ctx, b.Sale with { CustomerId = customer == ctx.WalkinId ? null : customer, StoreId = store }, store, retNet, ct);
        await uow.Sales.UpdateTotalsAsync(retId, await LinkAsync(uow, retId, saleId, ct), ct);
        await uow.Sales.UpdateTotalsAsync(saleId, await LinkAsync(uow, saleId, retId, ct), ct);
        await uow.CommitAsync(ct);
        await using var read = await uows.BeginAsync(u, ct);
        return new Row { ["duplicate"] = false, ["return"] = await DetailAsync(read, retId, ct), ["sale"] = await DetailAsync(read, saleId, ct) };
    }

    private static async Task<InvoiceTotalsUpdate> LinkAsync(IUnitOfWork uow, long id, long other, CancellationToken ct)
    {
        var h = (await uow.Sales.InvoiceAsync(id, ct))!;
        return new InvoiceTotalsUpdate(h.Get<long>("grossAmount"), h.Get<long>("lineDiscountAmount"), h.Get<long>("billDiscountAmount"), h.Get<long>("taxableAmount"), h.Get<long>("taxAmount"),
            h.Get<long>("netAmount"), h.Get<long>("paidAmount"), h.Get<long>("changeAmount"), h.Get<long>("creditAmount"), h.Get<long>("costAmount"), other);
    }

    // ------------------------------------------------------------------------------------------ void

    /// <summary>Cancels a posted invoice: the journal is reversed by the database, stock goes back here. The invoice stays on file as VOID.</summary>
    public async Task VoidAsync(UserContext u, long id, VoidBody b, CancellationToken ct)
    {
        u.Require(P.VoucherDelete);
        new Check().NotBlank(b.Reason, "reason", "Reason").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        var h = await uow.Sales.InvoiceAsync(id, ct) ?? throw new NotFoundException("Invoice", id);
        u.RequireStore(h.Get<long>("storeId"));
        if (h.Get<string>("status") != "POSTED") throw new BusinessRuleException("This invoice is already void.", "already_void");
        var isSale = h.Get<string>("docType") == "SALE";
        if (isSale && await uow.Sales.LiveReturnCountAsync(id, ct) > 0) throw new BusinessRuleException("This sale has returns against it. Void the returns first.", "has_returns");

        var store = h.Get<long>("storeId");
        var date = (await ContextAsync(u, uow, store, ct)).Today;
        foreach (var l in await uow.Sales.InvoiceLinesAsync(id, ct))
        {
            // A sale's stock goes back in; a return's stock goes out again (the ledger type SALE_VOID covers both, the sign tells which).
            var qty = l.Get<decimal>("qty");   // a sale line is positive (back in), a return line is negative (out again)
            await uow.StockMoves.PostMoveAsync(u.CompanyId, store, l.Get<long>("variantId"), qty, "SALE_VOID", id, l.Get<decimal>("unitCost"), date, ct);
        }
        await uow.Sales.VoidAsync(id, b.Reason!.Trim(), u.UserId, ct);
        await uow.CommitAsync(ct);
    }

    // ------------------------------------------------------------------------------------------ reads

    public async Task<Row> GetAsync(UserContext u, long id, CancellationToken ct)
    {
        u.RequireAny(P.SaleCreate, P.SaleReturn, P.ReportView, P.VoucherDelete, P.VoucherPost);
        await using var uow = await uows.BeginAsync(u, ct);
        return await DetailAsync(uow, id, ct);
    }

    /// <summary>Everything a printed receipt needs: the invoice with lines and payments, plus company terms and the store's address.</summary>
    public async Task<Row> ReceiptAsync(UserContext u, long id, CancellationToken ct)
    {
        u.RequireAny(P.SaleCreate, P.SaleReturn, P.ReportView, P.VoucherDelete);
        await using var uow = await uows.BeginAsync(u, ct);
        var inv = await DetailAsync(uow, id, ct);
        inv["company"] = await uow.Stores.GetCompanyAsync(u.CompanyId, ct);
        inv["storeInfo"] = await uow.Stores.GetAsync(u.CompanyId, inv.Get<long>("storeId"), ct);
        return inv;
    }

    /// <summary>Lines of a sale with how much of each can still be returned (the return screen).</summary>
    public async Task<Row> ReturnableAsync(UserContext u, long id, CancellationToken ct)
    {
        u.RequireAny(P.SaleReturn, P.SaleCreate);
        await using var uow = await uows.BeginAsync(u, ct);
        var inv = await DetailAsync(uow, id, ct);
        inv["returnable"] = await uow.Sales.ReturnableLinesAsync(id, ct);
        return inv;
    }

    public async Task<Page<Row>> ListAsync(UserContext u, SalesFilter f, PageQuery q, CancellationToken ct)
    {
        u.RequireAny(P.SaleCreate, P.SaleReturn, P.ReportView, P.VoucherDelete);
        if (f.StoreId is { } s) u.RequireStore(s);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Sales.InvoicesAsync(f, q, ct);
    }

    public async Task<List<Row>> DriftsAsync(UserContext u, CancellationToken ct)
    {
        u.RequireAny(P.DiscountApprove, P.ReportView);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Sales.OpenDriftsAsync(ct);
    }

    private static async Task<Row> DetailAsync(IUnitOfWork uow, long id, CancellationToken ct)
    {
        var h = await uow.Sales.InvoiceAsync(id, ct) ?? throw new NotFoundException("Invoice", id);
        h["lines"] = await uow.Sales.InvoiceLinesAsync(id, ct);
        h["payments"] = await uow.Sales.InvoicePaymentsAsync(id, ct);
        return h;
    }
}
