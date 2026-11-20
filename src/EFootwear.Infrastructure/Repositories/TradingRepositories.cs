using EFootwear.Application.Abstractions;
using EFootwear.Infrastructure.Persistence;

namespace EFootwear.Infrastructure.Repositories;

public sealed class SalesRepository(Db db) : ISalesRepository
{
    public Task<Row?> FindBySaleUuidAsync(Guid uuid, CancellationToken ct)
        => db.RowAsync("select id, invoice_no, doc_type, sale_date from sales.invoices where device_sale_uuid = @u", new { u = uuid }, ct);

    /// <summary>Everything a line copies from the catalogue, plus the price this store charges now (promotion output → store price → list).</summary>
    public Task<Row?> VariantSnapshotAsync(long variantId, long storeId, CancellationToken ct)
        => db.RowAsync(
            @"select v.id as variant_id, v.barcode, v.size_code, v.status as variant_status, p.id as product_id, p.name as product_name, p.status as product_status, p.tax_rate,
                     p.category_id, p.department_id, p.brand_id, p.season_id, p.gender_id, p.product_type_id, v.color_id, v.size_group_id,
                     coalesce(o.sale_price, vp.sale_price, 0) as list_price,
                     case when ep.company_id is not null and (ep.valid_until is null or ep.valid_until > now()) then ep.sale_price end as eff_price,
                     ep.rule_id, coalesce(ep.is_conflict, false) as is_conflict, ep.conflict_group_id
                from catalog.variants v
                join catalog.products p on p.company_id = v.company_id and p.id = v.product_id and not p.is_deleted
                left join catalog.variant_prices vp on vp.company_id = v.company_id and vp.variant_id = v.id
                left join catalog.store_price_overrides o on o.company_id = v.company_id and o.store_id = @s and o.variant_id = v.id
                left join promo.effective_prices ep on ep.company_id = v.company_id and ep.store_id = @s and ep.variant_id = v.id
                     and ep.channel_id = (select ch.id from integ.channels ch where ch.company_id = v.company_id and ch.code = 'POS')
               where v.id = @v and not v.is_deleted", new { v = variantId, s = storeId }, ct);

    public Task<long> NextNumberAsync(long companyId, long storeId, string series, DateOnly? period, CancellationToken ct)
        => db.ScalarAsync<long>("select core.next_number(@c, @s, @series, coalesce(@p::date, date '1900-01-01'))", new { c = companyId, s = storeId, series, p = period }, ct);

    public Task<long> InsertHeaderAsync(InvoiceHeaderInsert h, CancellationToken ct)
        => db.ScalarAsync<long>(
            @"insert into sales.invoices (company_id, store_id, channel_id, doc_type, invoice_no, sale_date, daily_bill_no, device_id, device_sale_uuid, occurred_at,
                    original_invoice_id, customer_party_id, salesman_party_id, walkin_name, walkin_mobile, online_order_ref, created_by)
              values (@c, @s, @ch, @dt, @no, @d, @daily, @dev, @uuid, @occ, @orig, @cust, @sm, @wn, @wm, @ref, core.current_user_id()) returning id",
            new { c = h.CompanyId, s = h.StoreId, ch = h.ChannelId, dt = h.DocType, no = h.InvoiceNo, d = h.SaleDate, daily = h.DailyBillNo, dev = h.DeviceId, uuid = h.DeviceSaleUuid,
                  occ = h.OccurredAt, orig = h.OriginalInvoiceId, cust = h.CustomerId, sm = h.SalesmanId, wn = h.WalkinName, wm = h.WalkinMobile, @ref = h.OnlineOrderRef }, ct);

    public Task<long> InsertLineAsync(InvoiceLineInsert l, CancellationToken ct)
        => db.ScalarAsync<long>(
            @"insert into sales.invoice_lines (company_id, invoice_id, line_no, store_id, channel_id, sale_date, doc_type, variant_id, product_id, category_id, department_id, brand_id,
                    season_id, gender_id, color_id, size_group_id, product_type_id, salesman_party_id, qty, list_price, unit_price, discount_source, rule_id, conflict_group_id,
                    bundle_id, manual_approved_by, discount_amount, tax_rate, taxable_amount, tax_amount, net_amount, unit_cost, cost_amount, original_line_id, original_line_sale_date)
              select @c, @inv, @no, @s, @ch, @d, @dt, v.id, p.id, p.category_id, p.department_id, p.brand_id, p.season_id, p.gender_id, v.color_id, v.size_group_id, p.product_type_id,
                     @sm, @q, @lp, @up, @src, @rule, @cg, @bun, @appr, @disc, @tr, @tx, @tax, @net, @uc, @cost, @ol, @old
                from catalog.variants v join catalog.products p on p.company_id = v.company_id and p.id = v.product_id
               where v.id = @v returning id",
            new { c = l.CompanyId, inv = l.InvoiceId, no = l.LineNo, s = l.StoreId, ch = l.ChannelId, d = l.SaleDate, dt = l.DocType, v = l.VariantId, sm = l.SalesmanId,
                  q = l.DocType == "RETURN" ? -l.Qty : l.Qty, lp = l.ListPrice, up = l.UnitPrice, src = l.DiscountSource, rule = l.RuleId, cg = l.ConflictGroupId, bun = l.BundleId,
                  appr = l.ManualApprovedBy, disc = l.DiscountAmount, tr = l.TaxRate, tx = l.Taxable, tax = l.Tax, net = l.Net, uc = l.UnitCost, cost = l.CostAmount,
                  ol = l.OriginalLineId, old = l.OriginalLineSaleDate }, ct);

    public Task UpdateTotalsAsync(long invoiceId, InvoiceTotalsUpdate t, CancellationToken ct)
        => db.ExecAsync(
            @"update sales.invoices set gross_amount = @g, line_discount_amount = @ld, bill_discount_amount = @bd, taxable_amount = @tx, tax_amount = @tax, net_amount = @n,
                    paid_amount = @p, change_amount = @ch, credit_amount = @cr, cost_amount = @cost, related_invoice_id = coalesce(@rel, related_invoice_id)
               where id = @id",
            new { id = invoiceId, g = t.Gross, ld = t.LineDiscount, bd = t.BillDiscount, tx = t.Taxable, tax = t.Tax, n = t.Net, p = t.Paid, ch = t.Change, cr = t.Credit, cost = t.Cost, rel = t.RelatedInvoiceId }, ct);

    public Task InsertPaymentAsync(PaymentInsert p, CancellationToken ct)
        => db.ExecAsync("insert into sales.payments (company_id, invoice_id, store_id, sale_date, method, payment_account_id, amount, reference) values (@c, @i, @s, @d, @m, @a, @amt, @r)",
            new { c = p.CompanyId, i = p.InvoiceId, s = p.StoreId, d = p.SaleDate, m = p.Method, a = p.AccountId, amt = p.Amount, r = p.Reference }, ct);

    public Task<long> RoleAccountAsync(string role, CancellationToken ct)
        => db.ScalarAsync<long>("select acct.role_account(core.current_company_id(), @r)", new { r = role }, ct);

    public Task<long?> WalkinPartyAsync(CancellationToken ct) => db.ScalarAsync<long?>("select id from party.parties where code = 'WALKIN' and not is_deleted", null, ct);

    public Task<Row?> PartyBriefAsync(long partyId, CancellationToken ct)
        => db.RowAsync("select id, code, name, is_customer, is_vendor, is_staff, status from party.parties where id = @id and not is_deleted", new { id = partyId }, ct);

    public Task<Row?> InvoiceAsync(long id, CancellationToken ct)
        => db.RowAsync(
            @"select i.id, i.store_id, s.name as store, i.channel_id, i.doc_type, i.invoice_no, i.daily_bill_no, i.sale_date, i.occurred_at, i.original_invoice_id, i.related_invoice_id,
                     i.customer_party_id, cu.name as customer, i.salesman_party_id, sm.name as salesman, i.walkin_name, i.walkin_mobile,
                     i.gross_amount, i.line_discount_amount, i.bill_discount_amount, i.taxable_amount, i.tax_amount, i.net_amount, i.paid_amount, i.change_amount, i.credit_amount, i.cost_amount,
                     i.has_price_drift, i.has_shortfall, i.status, i.void_reason, i.voided_at, i.journal_entry_id, i.created_by, i.created_at, i.row_ver
                from sales.invoices i
                join core.stores s on s.company_id = i.company_id and s.id = i.store_id
                left join party.parties cu on cu.company_id = i.company_id and cu.id = i.customer_party_id
                left join party.parties sm on sm.company_id = i.company_id and sm.id = i.salesman_party_id
               where i.id = @id", new { id }, ct);

    public Task<List<Row>> InvoiceLinesAsync(long invoiceId, CancellationToken ct)
        => db.RowsAsync(
            @"select l.id, l.line_no, l.variant_id, v.barcode, l.product_id, p.code as product_code, p.name as product_name, p.name_ur as product_name_ur, v.size_code, co.name as color,
                     l.qty, l.list_price, l.unit_price, l.discount_source, l.rule_id, l.bundle_id, l.discount_amount, l.tax_rate, l.taxable_amount, l.tax_amount, l.net_amount,
                     l.unit_cost, l.cost_amount, l.qty_short, l.original_line_id, l.original_line_sale_date, l.sale_date
                from sales.invoice_lines l
                join catalog.variants v on v.company_id = l.company_id and v.id = l.variant_id
                join catalog.products p on p.company_id = l.company_id and p.id = l.product_id
                left join master.colors co on co.company_id = l.company_id and co.id = l.color_id
               where l.invoice_id = @i order by l.line_no", new { i = invoiceId }, ct);

    public Task<List<Row>> InvoicePaymentsAsync(long invoiceId, CancellationToken ct)
        => db.RowsAsync("select id, method, payment_account_id, amount, reference, received_at from sales.payments where invoice_id = @i order by id", new { i = invoiceId }, ct);

    public Task<Page<Row>> InvoicesAsync(SalesFilter f, PageQuery q, CancellationToken ct)
        => db.PageAsync(
            @"select i.id, i.store_id, s.name as store, i.doc_type, i.invoice_no, i.daily_bill_no, i.sale_date, i.occurred_at, i.customer_party_id, cu.name as customer,
                     i.salesman_party_id, sm.name as salesman, i.net_amount, i.paid_amount, i.credit_amount, i.status, i.has_price_drift, i.has_shortfall
                from sales.invoices i
                join core.stores s on s.company_id = i.company_id and s.id = i.store_id
                left join party.parties cu on cu.company_id = i.company_id and cu.id = i.customer_party_id
                left join party.parties sm on sm.company_id = i.company_id and sm.id = i.salesman_party_id
               where (@st::bigint is null or i.store_id = @st) and (@f::date is null or i.sale_date >= @f) and (@t::date is null or i.sale_date <= @t)
                 and (@dt::text is null or i.doc_type = @dt) and (@status::text is null or i.status = @status) and (@cu::bigint is null or i.customer_party_id = @cu)
                 and (@sm::bigint is null or i.salesman_party_id = @sm) and (@no::bigint is null or i.invoice_no = @no)
                 and (@q::text is null or cu.name ilike @q or i.walkin_name ilike @q or i.walkin_mobile ilike @q)",
            "i.occurred_at desc, i.id desc", new { st = f.StoreId, f = f.From, t = f.To, dt = f.DocType, status = f.Status, cu = f.CustomerId, sm = f.SalesmanId, no = f.InvoiceNo, q = CatalogRepository.Like(q.Search) }, q, ct);

    public Task<List<Row>> ReturnableLinesAsync(long invoiceId, CancellationToken ct)
        => db.RowsAsync(
            @"select l.id, l.sale_date, l.line_no, l.variant_id, l.qty, l.list_price, l.unit_price, l.discount_source, l.rule_id, l.bundle_id, l.discount_amount, l.tax_rate,
                     l.taxable_amount, l.tax_amount, l.net_amount, l.unit_cost, l.salesman_party_id, l.list_price * l.qty as gross_value,
                     coalesce((select sum(-r.qty) from sales.invoice_lines r join sales.invoices h on h.company_id = r.company_id and h.id = r.invoice_id
                                where r.company_id = l.company_id and r.doc_type = 'RETURN' and r.original_line_id = l.id and r.original_line_sale_date = l.sale_date and h.status <> 'VOID'), 0) as returned_qty
                from sales.invoice_lines l where l.invoice_id = @i and l.doc_type = 'SALE' order by l.line_no", new { i = invoiceId }, ct);

    public async Task<decimal> ReturnedQtyAsync(long originalLineId, CancellationToken ct)
        => await db.ScalarAsync<decimal?>(
            @"select sum(-r.qty) from sales.invoice_lines r join sales.invoices h on h.company_id = r.company_id and h.id = r.invoice_id
               where r.doc_type = 'RETURN' and r.original_line_id = @l and h.status <> 'VOID'", new { l = originalLineId }, ct) ?? 0;

    public Task<long> LiveReturnCountAsync(long invoiceId, CancellationToken ct)
        => db.ScalarAsync<long>("select count(*) from sales.invoices where original_invoice_id = @i and doc_type = 'RETURN' and status <> 'VOID'", new { i = invoiceId }, ct);

    public Task VoidAsync(long invoiceId, string reason, long userId, CancellationToken ct)
        => db.ExecAsync("update sales.invoices set status = 'VOID', void_reason = @r, voided_by = @u, voided_at = now() where id = @i and status = 'POSTED'", new { i = invoiceId, r = reason, u = userId }, ct);

    public Task<List<Row>> OpenDriftsAsync(CancellationToken ct)
        => db.RowsAsync(
            @"select d.id, d.store_id, d.invoice_id, i.invoice_no, d.variant_id, v.barcode, d.occurred_at, d.qty, d.expected_unit_price, d.charged_unit_price, d.difference_amount, d.reason
                from sales.price_drift_records d join sales.invoices i on i.company_id = d.company_id and i.id = d.invoice_id
                join catalog.variants v on v.company_id = d.company_id and v.id = d.variant_id where d.status = 'OPEN' order by d.occurred_at desc", null, ct);
}

public sealed class PurchaseRepository(Db db) : IPurchaseRepository
{
    public Task<long> NextNumberAsync(long companyId, long storeId, string series, CancellationToken ct)
        => db.ScalarAsync<long>("select core.next_number(@c, @s, @series)", new { c = companyId, s = storeId, series }, ct);

    public Task<long> InsertHeaderAsync(PurchaseHeaderInsert h, CancellationToken ct)
        => db.ScalarAsync<long>(
            @"insert into purch.invoices (company_id, store_id, doc_type, invoice_no, vendor_bill_no, vendor_party_id, bill_date, received_on, due_date, gross_amount, discount_amount, tax_amount, net_amount, created_by)
              values (@c, @s, @dt, @no, @vb, @v, @bd, @ro, @dd, @g, @d, @t, @n, core.current_user_id()) returning id",
            new { c = h.CompanyId, s = h.StoreId, dt = h.DocType, no = h.InvoiceNo, vb = h.VendorBillNo, v = h.VendorId, bd = h.BillDate, ro = h.ReceivedOn, dd = h.DueDate, g = h.Gross, d = h.Discount, t = h.Tax, n = h.Net }, ct);

    public Task<long> InsertLineAsync(PurchaseLineInsert l, CancellationToken ct)
        => db.ScalarAsync<long>(
            @"insert into purch.invoice_lines (company_id, invoice_id, line_no, variant_id, qty, unit_cost, sale_price, sale_discount_pct, line_amount, updates_sale_price, original_line_id)
              values (@c, @i, @no, @v, @q, @uc, @sp, @sd, @la, @us, @ol) returning id",
            new { c = l.CompanyId, i = l.InvoiceId, no = l.LineNo, v = l.VariantId, q = l.Qty, uc = l.UnitCost, sp = l.SalePrice, sd = l.SaleDiscountPct, la = l.LineAmount, us = l.UpdatesSalePrice, ol = l.OriginalLineId }, ct);

    public Task<Row?> VendorAsync(long id, CancellationToken ct)
        => db.RowAsync("select id, code, name, is_vendor, status, credit_limit from party.parties where id = @id and not is_deleted", new { id }, ct);

    public Task<Row?> InvoiceAsync(long id, CancellationToken ct)
        => db.RowAsync(
            @"select i.id, i.store_id, s.name as store, i.doc_type, i.invoice_no, i.vendor_bill_no, i.vendor_party_id, ve.name as vendor, i.bill_date, i.received_on, i.due_date,
                     i.gross_amount, i.discount_amount, i.tax_amount, i.net_amount, i.status, i.void_reason, i.voided_at, i.journal_entry_id, i.created_at, i.row_ver
                from purch.invoices i join core.stores s on s.company_id = i.company_id and s.id = i.store_id
                join party.parties ve on ve.company_id = i.company_id and ve.id = i.vendor_party_id where i.id = @id", new { id }, ct);

    public Task<List<Row>> InvoiceLinesAsync(long invoiceId, CancellationToken ct)
        => db.RowsAsync(
            @"select l.id, l.line_no, l.variant_id, v.barcode, p.code as product_code, p.name as product_name, v.size_code, l.qty, l.unit_cost, l.sale_price, l.sale_discount_pct,
                     l.line_amount, l.updates_sale_price, l.original_line_id,
                     coalesce((select sum(-r.qty) from purch.invoice_lines r join purch.invoices h on h.company_id = r.company_id and h.id = r.invoice_id
                                where r.company_id = l.company_id and r.original_line_id = l.id and h.status <> 'VOID'), 0) as returned_qty
                from purch.invoice_lines l join catalog.variants v on v.company_id = l.company_id and v.id = l.variant_id
                join catalog.products p on p.company_id = v.company_id and p.id = v.product_id where l.invoice_id = @i order by l.line_no", new { i = invoiceId }, ct);

    public Task<Page<Row>> InvoicesAsync(PurchaseFilter f, PageQuery q, CancellationToken ct)
        => db.PageAsync(
            @"select i.id, i.store_id, s.name as store, i.doc_type, i.invoice_no, i.vendor_bill_no, i.vendor_party_id, ve.name as vendor, i.bill_date, i.due_date, i.gross_amount, i.discount_amount,
                     i.tax_amount, i.net_amount, i.status
                from purch.invoices i join core.stores s on s.company_id = i.company_id and s.id = i.store_id
                join party.parties ve on ve.company_id = i.company_id and ve.id = i.vendor_party_id
               where (@st::bigint is null or i.store_id = @st) and (@f::date is null or i.bill_date >= @f) and (@t::date is null or i.bill_date <= @t)
                 and (@v::bigint is null or i.vendor_party_id = @v) and (@dt::text is null or i.doc_type = @dt) and (@status::text is null or i.status = @status)
                 and (@q::text is null or ve.name ilike @q or i.vendor_bill_no ilike @q or i.invoice_no::text = @raw)",
            "i.bill_date desc, i.id desc", new { st = f.StoreId, f = f.From, t = f.To, v = f.VendorId, dt = f.DocType, status = f.Status, q = CatalogRepository.Like(f.Search), raw = f.Search?.Trim() }, q, ct);

    public Task<Row?> LineAsync(long lineId, CancellationToken ct)
        => db.RowAsync(
            @"select l.id, l.invoice_id, l.variant_id, l.qty, l.unit_cost, l.sale_price, l.sale_discount_pct, i.doc_type, i.status, i.store_id, i.vendor_party_id, i.net_amount, i.gross_amount,
                     coalesce((select sum(-r.qty) from purch.invoice_lines r join purch.invoices h on h.company_id = r.company_id and h.id = r.invoice_id
                                where r.company_id = l.company_id and r.original_line_id = l.id and h.status <> 'VOID'), 0) as returned_qty
                from purch.invoice_lines l join purch.invoices i on i.company_id = l.company_id and i.id = l.invoice_id where l.id = @id", new { id = lineId }, ct);

    public Task<long> LiveReturnCountAsync(long invoiceId, CancellationToken ct)
        => db.ScalarAsync<long>(
            @"select count(distinct h.id) from purch.invoice_lines r join purch.invoice_lines o on o.company_id = r.company_id and o.id = r.original_line_id
               join purch.invoices h on h.company_id = r.company_id and h.id = r.invoice_id where o.invoice_id = @i and h.status <> 'VOID'", new { i = invoiceId }, ct);

    public Task VoidAsync(long invoiceId, string reason, long userId, CancellationToken ct)
        => db.ExecAsync("update purch.invoices set status = 'VOID', void_reason = @r, voided_by = @u, voided_at = now() where id = @i and status = 'POSTED'", new { i = invoiceId, r = reason, u = userId }, ct);

    public Task<Row?> VariantAsync(long variantId, CancellationToken ct)
        => db.RowAsync(
            @"select v.id as variant_id, v.barcode, v.status, p.name as product_name, v.size_code, coalesce(vp.sale_price, 0) as sale_price, coalesce(vp.purchase_price, 0) as purchase_price
                from catalog.variants v join catalog.products p on p.company_id = v.company_id and p.id = v.product_id and not p.is_deleted
                left join catalog.variant_prices vp on vp.company_id = v.company_id and vp.variant_id = v.id where v.id = @id and not v.is_deleted", new { id = variantId }, ct);

    public Task<List<Row>> VendorBillsAsync(long vendorId, CancellationToken ct)
        => db.RowsAsync("select id, invoice_no, vendor_bill_no, bill_date, due_date, doc_type, net_amount, status from purch.invoices where vendor_party_id = @v and status = 'POSTED' order by bill_date desc, id desc", new { v = vendorId }, ct);
}

public sealed class StockMoves(Db db) : IStockMoves
{
    public async Task<decimal> PostMoveAsync(long companyId, long storeId, long variantId, decimal qty, string docType, long docId, decimal unitCost, DateOnly date, CancellationToken ct)
        => await db.ScalarAsync<decimal>("select inv.post_stock_move(@c, @s, @v, @q, @dt, @di, @uc, @d)",
            new { c = companyId, s = storeId, v = variantId, q = qty, dt = docType, di = docId, uc = unitCost, d = date }, ct);

    public async Task<Dictionary<long, decimal>> OnHandAsync(long storeId, IReadOnlyCollection<long> variantIds, CancellationToken ct)
    {
        var rows = await db.RowsAsync("select variant_id, qty_on_hand from inv.stock_balances where store_id = @s and variant_id = any(@v)", new { s = storeId, v = variantIds.ToArray() }, ct);
        return rows.ToDictionary(r => r.Get<long>("variantId"), r => r.Get<decimal>("qtyOnHand"));
    }
}
