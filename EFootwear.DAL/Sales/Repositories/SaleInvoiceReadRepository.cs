using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Sales.Abstractions;
using EFootwear.BLL.Sales.Contracts;
using EFootwear.DAL.Common;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Common;
using EFootwear.Domain.Core;
using EFootwear.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Sales.Repositories;

/// <summary>
/// Reads of the Sales module. Plain SQL over the sales tables, the catalogue, the customers and the staff; row-level security already limits every answer to the company
/// and the stores of the caller. A line of a sale is always read together with the sale date of its invoice so the partitioned line table is pruned.
/// Text searches use <c>position(lower(text) in lower(column))</c> so a percent sign or an underscore typed by the user is never a wildcard.
/// </summary>
public sealed class SaleInvoiceReadRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : ISaleInvoiceReadRepository
{
    // ------------------------------------------------------------------ invoice detail

    public async Task<SaleInvoiceResponse?> FindInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;

        var header = await databaseContext.Database.SqlQuery<InvoiceHeaderRow>($"""
            select i.id as "Id", i.store_id as "StoreId", s.name as "StoreName", i.doc_type as "DocumentTypeCode", i.invoice_no as "InvoiceNumber",
                   i.daily_bill_no as "DailyBillNumber", i.sale_date as "SaleDate", i.occurred_at as "OccurredAt",
                   i.customer_id as "CustomerId", customer.name as "CustomerName", i.walkin_name as "WalkInCustomerName",
                   i.walkin_mobile as "WalkInCustomerMobile", i.salesman_staff_id as "SalesmanStaffId", salesman.name as "SalesmanName",
                   d.label as "PosTerminalLabel", x.description as "Description",
                   cast(i.gross_amount as bigint) as "GrossAmount",
                   cast(i.line_discount_amount + i.bill_discount_amount as bigint) as "DiscountAmount",
                   cast(i.tax_amount as bigint) as "TaxAmount", cast(i.net_amount as bigint) as "NetAmount",
                   cast(i.paid_amount as bigint) as "PaidAmount", cast(i.change_amount as bigint) as "CashReturnedAmount",
                   cast(i.credit_amount as bigint) as "OutstandingAmount",
                   i.status as "StatusCode", i.void_reason as "VoidReason", i.voided_at as "VoidedAt", voider.full_name as "VoidedByName",
                   creator.full_name as "CreatedByName",
                   i.original_invoice_id as "OriginalInvoiceId", original.invoice_no as "OriginalInvoiceNumber",
                   i.related_invoice_id as "RelatedInvoiceId", related.invoice_no as "RelatedInvoiceNumber",
                   x.replaces_invoice_id as "ReplacesInvoiceId", replaced.invoice_no as "ReplacesInvoiceNumber",
                   i.row_ver as "RowVersion"
              from sales.invoices i
              join core.stores s on s.company_id = i.company_id and s.id = i.store_id
              left join contacts.customers customer on customer.company_id = i.company_id and customer.id = i.customer_id
              left join contacts.staff salesman on salesman.company_id = i.company_id and salesman.id = i.salesman_staff_id
              left join core.devices d on d.company_id = i.company_id and d.id = i.device_id
              left join sales.invoice_extras x on x.company_id = i.company_id and x.invoice_id = i.id
              left join sec.users voider on voider.company_id = i.company_id and voider.id = i.voided_by
              left join sec.users creator on creator.company_id = i.company_id and creator.id = i.created_by
              left join sales.invoices original on original.company_id = i.company_id and original.id = i.original_invoice_id
              left join sales.invoices related on related.company_id = i.company_id and related.id = i.related_invoice_id
              left join sales.invoices replaced on replaced.company_id = i.company_id and replaced.id = x.replaces_invoice_id
             where i.company_id = {companyId} and i.id = {invoiceId}
            """).SingleOrDefaultAsync(cancellationToken);
        if (header is null) return null;

        var lineRows = await databaseContext.Database.SqlQuery<InvoiceLineRow>($"""
            select il.id as "LineId", il.line_no as "LineNumber", il.variant_id as "VariantId", il.product_id as "ProductId",
                   p.code as "ProductCode", p.name as "ProductName", p.name_ur as "ProductNameUrdu", u.name as "UnitName",
                   v.size_code as "SizeCode", v.barcode as "Barcode", il.qty as "Quantity",
                   cast(il.list_price as bigint) as "ListPrice", cast(il.unit_price as bigint) as "UnitPrice",
                   cast(il.list_price - il.unit_price as bigint) as "DiscountPerUnit",
                   case when il.list_price > 0 then round((il.list_price - il.unit_price) * 100.0 / il.list_price, 2) else 0 end as "DiscountPercent",
                   cast(il.discount_amount as bigint) as "DiscountAmount", cast(il.net_amount as bigint) as "NetAmount",
                   il.discount_source as "DiscountSourceCode", il.original_line_id as "OriginalLineId", original.invoice_no as "OriginalInvoiceNumber",
                   e.return_reason as "ReturnReason", e.item_condition as "ItemConditionCode"
              from sales.invoice_lines il
              join catalog.variants v on v.company_id = il.company_id and v.id = il.variant_id
              join catalog.products p on p.company_id = il.company_id and p.id = il.product_id
              left join master.units u on u.company_id = p.company_id and u.id = p.unit_id
              left join sales.invoice_line_extras e on e.company_id = il.company_id and e.line_id = il.id and e.line_sale_date = il.sale_date
              left join sales.invoice_lines ol on ol.company_id = il.company_id and ol.id = il.original_line_id and ol.sale_date = il.original_line_sale_date
              left join sales.invoices original on original.company_id = ol.company_id and original.id = ol.invoice_id
             where il.company_id = {companyId} and il.invoice_id = {invoiceId} and il.sale_date = {header.SaleDate}
             order by il.line_no
            """).ToListAsync(cancellationToken);

        var paymentRows = await databaseContext.Database.SqlQuery<InvoicePaymentRow>($"""
            select pay.id as "Id", pay.method as "PaymentMethodCode", pay.payment_account_id as "PaymentAccountId", a.name as "PaymentAccountTitle",
                   cast(pay.amount as bigint) as "Amount", pay.reference as "ReferenceNumber"
              from sales.payments pay
              join acct.accounts a on a.company_id = pay.company_id and a.id = pay.payment_account_id
             where pay.company_id = {companyId} and pay.invoice_id = {invoiceId} and pay.sale_date = {header.SaleDate}
             order by pay.received_at, pay.id
            """).ToListAsync(cancellationToken);

        var lines = lineRows.Select(row => new SaleInvoiceLineResponse(
            row.LineId, row.LineNumber, row.VariantId, row.ProductId, row.ProductCode, row.ProductName, row.ProductNameUrdu, row.UnitName, row.SizeCode,
            row.Barcode, row.Quantity, row.ListPrice, row.UnitPrice, row.DiscountPerUnit, row.DiscountPercent, row.DiscountAmount, row.NetAmount,
            row.DiscountSourceCode, row.OriginalLineId, row.OriginalInvoiceNumber, row.ReturnReason, row.ItemConditionCode)).ToList();
        var payments = paymentRows.Select(row => new SaleInvoicePaymentResponse(
            row.Id, row.PaymentMethodCode, row.PaymentAccountId, row.PaymentAccountTitle, row.Amount, row.ReferenceNumber)).ToList();

        return new SaleInvoiceResponse(
            header.Id, header.StoreId, header.StoreName, header.DocumentTypeCode, header.InvoiceNumber, header.DailyBillNumber, header.SaleDate,
            header.OccurredAt, header.CustomerId, header.CustomerName, header.WalkInCustomerName, header.WalkInCustomerMobile,
            header.SalesmanStaffId, header.SalesmanName, header.PosTerminalLabel, header.Description, header.GrossAmount, header.DiscountAmount,
            header.TaxAmount, header.NetAmount, header.PaidAmount, header.CashReturnedAmount, header.OutstandingAmount, header.StatusCode,
            header.VoidReason, header.VoidedAt, header.VoidedByName, header.CreatedByName, header.OriginalInvoiceId, header.OriginalInvoiceNumber,
            header.RelatedInvoiceId, header.RelatedInvoiceNumber, header.ReplacesInvoiceId, header.ReplacesInvoiceNumber, header.RowVersion, lines, payments);
    }

    // ------------------------------------------------------------------ list of invoices

    public async Task<PagedResult<SaleInvoiceListItemResponse>> SearchAsync(
        SaleInvoiceSearchFilter filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var searchText = pageRequest.SearchText;
        var isSearchTextNumber = long.TryParse(searchText, out var searchNumber);
        var orderBy = BuildOrderBy(pageRequest);
        var skippedCount = pageRequest.SkippedItemCount;
        var pageSize = pageRequest.PageSize;

        // The same filter text twice (count and page): kept in one place so the two can never drift apart.
        var totalCount = await databaseContext.Database.SqlQuery<long>($"""
            select count(*) as "Value"
              from sales.invoices i
              left join contacts.customers customer on customer.company_id = i.company_id and customer.id = i.customer_id
             where i.company_id = {companyId}
               and (cast({filter.StoreId} as uuid) is null or i.store_id = cast({filter.StoreId} as uuid))
               and (cast({filter.FromDate} as date) is null or i.sale_date >= cast({filter.FromDate} as date))
               and (cast({filter.ToDate} as date) is null or i.sale_date <= cast({filter.ToDate} as date))
               and (cast({filter.InvoiceNumber} as bigint) is null or i.invoice_no = cast({filter.InvoiceNumber} as bigint))
               and (cast({filter.DocumentTypeCode} as text) is null or i.doc_type = cast({filter.DocumentTypeCode} as text))
               and (cast({filter.StatusCode} as text) is null or i.status = cast({filter.StatusCode} as text))
               and (cast({filter.CustomerId} as uuid) is null or i.customer_id = cast({filter.CustomerId} as uuid))
               and (cast({filter.SalesmanStaffId} as uuid) is null or i.salesman_staff_id = cast({filter.SalesmanStaffId} as uuid))
               and (cast({filter.MinimumNetAmount} as bigint) is null or abs(i.net_amount) >= cast({filter.MinimumNetAmount} as bigint))
               and (cast({filter.MaximumNetAmount} as bigint) is null or abs(i.net_amount) <= cast({filter.MaximumNetAmount} as bigint))
               and (cast({searchText} as text) is null
                    or (cast({isSearchTextNumber} as boolean) and i.invoice_no = {searchNumber})
                    or position(lower(cast({searchText} as text)) in lower(coalesce(customer.name, ''))) > 0
                    or position(lower(cast({searchText} as text)) in lower(coalesce(i.walkin_name, ''))) > 0)
            """).SingleAsync(cancellationToken);
        if (totalCount == 0) return PagedResult<SaleInvoiceListItemResponse>.Empty(pageRequest);

        var rows = await databaseContext.Database.SqlQueryRaw<InvoiceListRow>($$"""
            select i.id as "Id", i.store_id as "StoreId", s.name as "StoreName", i.doc_type as "DocumentTypeCode", i.invoice_no as "InvoiceNumber",
                   i.daily_bill_no as "DailyBillNumber", i.sale_date as "SaleDate", customer.name as "CustomerName", i.walkin_name as "WalkInCustomerName",
                   salesman.name as "SalesmanName", cast(i.net_amount as bigint) as "NetAmount", cast(i.paid_amount as bigint) as "PaidAmount",
                   cast(i.credit_amount as bigint) as "OutstandingAmount", i.status as "StatusCode",
                   (i.related_invoice_id is not null) as "IsPartOfExchange", i.void_reason as "VoidReason", i.voided_at as "VoidedAt",
                   voider.full_name as "VoidedByName"
              from sales.invoices i
              join core.stores s on s.company_id = i.company_id and s.id = i.store_id
              left join contacts.customers customer on customer.company_id = i.company_id and customer.id = i.customer_id
              left join contacts.staff salesman on salesman.company_id = i.company_id and salesman.id = i.salesman_staff_id
              left join sec.users voider on voider.company_id = i.company_id and voider.id = i.voided_by
             where i.company_id = {0}
               and ({1}::uuid is null or i.store_id = {1}::uuid)
               and ({2}::date is null or i.sale_date >= {2}::date)
               and ({3}::date is null or i.sale_date <= {3}::date)
               and ({4}::bigint is null or i.invoice_no = {4}::bigint)
               and ({5}::text is null or i.doc_type = {5}::text)
               and ({6}::text is null or i.status = {6}::text)
               and ({7}::uuid is null or i.customer_id = {7}::uuid)
               and ({8}::uuid is null or i.salesman_staff_id = {8}::uuid)
               and ({9}::bigint is null or abs(i.net_amount) >= {9}::bigint)
               and ({10}::bigint is null or abs(i.net_amount) <= {10}::bigint)
               and ({11}::text is null
                    or ({12}::boolean and i.invoice_no = {13}::bigint)
                    or position(lower({11}::text) in lower(coalesce(customer.name, ''))) > 0
                    or position(lower({11}::text) in lower(coalesce(i.walkin_name, ''))) > 0)
             order by {{orderBy}}
             offset {14} limit {15}
            """,
            companyId, (object?)filter.StoreId ?? DBNull.Value, (object?)filter.FromDate ?? DBNull.Value, (object?)filter.ToDate ?? DBNull.Value,
            (object?)filter.InvoiceNumber ?? DBNull.Value, (object?)filter.DocumentTypeCode ?? DBNull.Value, (object?)filter.StatusCode ?? DBNull.Value,
            (object?)filter.CustomerId ?? DBNull.Value, (object?)filter.SalesmanStaffId ?? DBNull.Value,
            (object?)filter.MinimumNetAmount ?? DBNull.Value, (object?)filter.MaximumNetAmount ?? DBNull.Value,
            (object?)searchText ?? DBNull.Value, isSearchTextNumber, searchNumber, skippedCount, pageSize).ToListAsync(cancellationToken);

        var items = rows.Select(row => new SaleInvoiceListItemResponse(
            row.Id, row.StoreId, row.StoreName, row.DocumentTypeCode, row.InvoiceNumber, row.DailyBillNumber, row.SaleDate, row.CustomerName,
            row.WalkInCustomerName, row.SalesmanName, row.NetAmount, row.PaidAmount, row.OutstandingAmount, row.StatusCode, row.IsPartOfExchange,
            row.VoidReason, row.VoidedAt, row.VoidedByName)).ToList();
        return new PagedResult<SaleInvoiceListItemResponse>(items, pageRequest.PageNumber, pageRequest.PageSize, totalCount);
    }

    // Only names from this fixed list reach the SQL text: the sort field of the request is never copied into it.
    private static string BuildOrderBy(PageRequest pageRequest)
    {
        var direction = pageRequest.SortDirection == SortDirection.Descending ? "desc" : "asc";
        return pageRequest.SortField?.ToLowerInvariant() switch
        {
            "invoicenumber" => $"i.invoice_no {direction}, i.id",
            "netamount" => $"abs(i.net_amount) {direction}, i.invoice_no desc",
            "customername" => $"customer.name {direction} nulls last, i.invoice_no desc",
            "saledate" => $"i.sale_date {direction}, i.invoice_no {direction}, i.id",
            _ => "i.sale_date desc, i.occurred_at desc, i.invoice_no desc, i.id",
        };
    }

    // ------------------------------------------------------------------ purchase history of a customer

    public async Task<PagedResult<SaleCustomerPurchaseResponse>> ListCustomerPurchasesAsync(
        Guid customerId, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        // The invoices are the ones View Invoices would list for this customer (posted only, newest first).
        var invoices = await SearchAsync(
            new SaleInvoiceSearchFilter(CustomerId: customerId, StatusCode: SaleInvoiceStatusCodes.Posted),
            new PageRequest(pageRequest.PageNumber, pageRequest.PageSize), cancellationToken);
        if (invoices.Items.Count == 0)
            return new PagedResult<SaleCustomerPurchaseResponse>([], invoices.PageNumber, invoices.PageSize, invoices.TotalItemCount);

        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var invoiceIds = invoices.Items.Select(invoice => invoice.Id).ToArray();
        var saleDates = invoices.Items.Select(invoice => invoice.SaleDate).Distinct().ToArray();   // prunes the partitioned line table
        var lineRows = await databaseContext.Database.SqlQuery<CustomerPurchaseLineRow>($"""
            select il.invoice_id as "InvoiceId", il.id as "LineId", p.name as "ProductName", v.size_code as "SizeCode", col.name as "ColorName",
                   il.qty as "Quantity", cast(il.unit_price as bigint) as "UnitPrice", cast(il.net_amount as bigint) as "NetAmount"
              from sales.invoice_lines il
              join catalog.variants v on v.company_id = il.company_id and v.id = il.variant_id
              join catalog.products p on p.company_id = il.company_id and p.id = il.product_id
              left join master.colors col on col.company_id = v.company_id and col.id = v.color_id
             where il.company_id = {companyId} and il.invoice_id = any({invoiceIds}) and il.sale_date = any({saleDates})
             order by il.line_no
            """).ToListAsync(cancellationToken);
        var linesByInvoice = lineRows.ToLookup(row => row.InvoiceId);

        var items = invoices.Items.Select(invoice => new SaleCustomerPurchaseResponse(
            invoice.Id, invoice.StoreName, invoice.DocumentTypeCode, invoice.InvoiceNumber, invoice.SaleDate, invoice.NetAmount, invoice.PaidAmount,
            invoice.OutstandingAmount,
            linesByInvoice[invoice.Id].Select(row => new SaleCustomerPurchaseLineResponse(
                row.LineId, row.ProductName, row.SizeCode, row.ColorName, row.Quantity, row.UnitPrice, row.NetAmount)).ToList())).ToList();
        return new PagedResult<SaleCustomerPurchaseResponse>(items, invoices.PageNumber, invoices.PageSize, invoices.TotalItemCount);
    }

    // ------------------------------------------------------------------ items

    // Same pricing as the posting function: the store price (or the list price) is the shelf price; the promotion engine row lowers it only when it applies.
    private const string ItemSelectSql = """
        select va.id as "VariantId", pr.id as "ProductId", pr.code as "ProductCode", pr.name as "ProductName", pr.name_ur as "ProductNameUrdu",
               u.name as "UnitName", va.size_code as "SizeCode", col.name as "ColorName", va.barcode as "Barcode",
               coalesce(sb.qty_on_hand, 0) as "StockQuantity",
               cast(coalesce(o.sale_price, vp.sale_price) as bigint) as "ListPrice",
               cast(case when engine_applies.applies then ep.sale_price else coalesce(o.sale_price, vp.sale_price) end as bigint) as "SuggestedUnitPrice",
               cast(case when engine_applies.applies then coalesce(o.sale_price, vp.sale_price) - ep.sale_price else 0 end as bigint) as "PromotionDiscountPerUnit",
               coalesce(ep.is_conflict, false) as "HasPromotionConflict",
               pr.tax_rate as "TaxRatePercentage",
               exists (select 1 from catalog.product_images im where im.company_id = pr.company_id and im.product_id = pr.id and im.slot = 1) as "HasPicture"
          from catalog.variants va
          join catalog.products pr on pr.company_id = va.company_id and pr.id = va.product_id
          join catalog.variant_prices vp on vp.company_id = va.company_id and vp.variant_id = va.id
          left join master.units u on u.company_id = pr.company_id and u.id = pr.unit_id
          left join master.colors col on col.company_id = va.company_id and col.id = va.color_id
          left join inv.stock_balances sb on sb.company_id = va.company_id and sb.store_id = {1} and sb.variant_id = va.id
          left join catalog.store_price_overrides o on o.company_id = va.company_id and o.store_id = {1} and o.variant_id = va.id
          left join integ.channels ch on ch.company_id = va.company_id and ch.code = 'POS' and ch.is_active
          left join promo.effective_prices ep on ep.company_id = va.company_id and ep.channel_id = ch.id and ep.store_id = {1} and ep.variant_id = va.id
          cross join lateral (select (ep.rule_id is not null and not ep.is_conflict and ep.list_price = coalesce(o.sale_price, vp.sale_price)
                                      and (ep.valid_until is null or ep.valid_until > now())) as applies) engine_applies
         where va.company_id = {0} and not va.is_deleted and va.status = 'ACTIVE' and not pr.is_deleted and pr.status = 'ACTIVE'
        """;

    public async Task<SaleItemResponse?> FindItemByBarcodeAsync(Guid storeId, string barcode, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var storedBarcode = await BarcodeMatching.FindStoredBarcodeAsync(databaseContext, currentUser.CompanyId, barcode, cancellationToken);
        var rows = await databaseContext.Database.SqlQueryRaw<SaleItemRow>(
            ItemSelectSql + " and va.barcode = {2} limit 1", currentUser.CompanyId, storeId, storedBarcode).ToListAsync(cancellationToken);
        return rows.Select(ToItemResponse).FirstOrDefault();
    }

    public async Task<IReadOnlyList<SaleItemResponse>> SearchItemsAsync(Guid storeId, string searchText, int limit, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var rows = await databaseContext.Database.SqlQueryRaw<SaleItemRow>(
            ItemSelectSql + """
               and (position(lower({2}) in lower(pr.name)) > 0 or position(lower({2}) in lower(pr.code)) > 0 or position(lower({2}) in lower(va.barcode)) > 0
                    or position(lower({2}) in lower(coalesce(pr.name_ur, ''))) > 0)
             order by pr.name, va.size_code
             limit {3}
            """, currentUser.CompanyId, storeId, searchText, limit).ToListAsync(cancellationToken);
        return rows.Select(ToItemResponse).ToList();
    }

    private static SaleItemResponse ToItemResponse(SaleItemRow row) => new(
        row.VariantId, row.ProductId, row.ProductCode, row.ProductName, row.ProductNameUrdu, row.UnitName, row.SizeCode, row.ColorName, row.Barcode,
        row.StockQuantity, row.ListPrice, row.SuggestedUnitPrice, row.PromotionDiscountPerUnit, row.HasPromotionConflict, row.TaxRatePercentage, row.HasPicture);

    // ------------------------------------------------------------------ returnable lines

    public async Task<ReturnableInvoiceResponse?> FindReturnableInvoiceAsync(Guid storeId, long invoiceNumber, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var header = await databaseContext.Database.SqlQuery<ReturnableHeaderRow>($"""
            select i.id as "InvoiceId", i.invoice_no as "InvoiceNumber", i.store_id as "StoreId", i.sale_date as "SaleDate",
                   i.customer_id as "CustomerId", customer.name as "CustomerName", i.walkin_name as "WalkInCustomerName"
              from sales.invoices i
              left join contacts.customers customer on customer.company_id = i.company_id and customer.id = i.customer_id
             where i.company_id = {companyId} and i.store_id = {storeId} and i.doc_type = 'SALE' and i.invoice_no = {invoiceNumber} and i.status = 'POSTED'
            """).SingleOrDefaultAsync(cancellationToken);
        if (header is null) return null;

        var lines = await QueryReturnableLinesAsync(header.InvoiceId, [], false, cancellationToken);
        return new ReturnableInvoiceResponse(
            header.InvoiceId, header.InvoiceNumber, header.StoreId, header.SaleDate, header.CustomerId, header.CustomerName, header.WalkInCustomerName, lines);
    }

    public Task<IReadOnlyList<ReturnableSaleLineResponse>> FindReturnableLinesAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken cancellationToken) =>
        lineIds.Count == 0
            ? Task.FromResult<IReadOnlyList<ReturnableSaleLineResponse>>([])
            : QueryReturnableLinesAsync(Guid.Empty, lineIds.ToArray(), true, cancellationToken);

    private async Task<IReadOnlyList<ReturnableSaleLineResponse>> QueryReturnableLinesAsync(
        Guid invoiceId, Guid[] lineIds, bool useLineIds, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        // A voided return does not count: the goods did not come back (the void put the stock out again).
        var rows = await databaseContext.Database.SqlQuery<ReturnableLineRow>($"""
            select il.id as "LineId", il.line_no as "LineNumber", il.invoice_id as "InvoiceId", i.invoice_no as "InvoiceNumber",
                   il.variant_id as "VariantId", p.code as "ProductCode", p.name as "ProductName", p.name_ur as "ProductNameUrdu",
                   u.name as "UnitName", v.size_code as "SizeCode", v.barcode as "Barcode",
                   il.qty as "SoldQuantity", coalesce(r.returned_quantity, 0) as "AlreadyReturnedQuantity",
                   il.qty - coalesce(r.returned_quantity, 0) as "ReturnableQuantity",
                   cast(il.list_price as bigint) as "ListPrice", cast(il.unit_price as bigint) as "UnitPrice",
                   cast(il.list_price - il.unit_price as bigint) as "DiscountPerUnit",
                   case when il.list_price > 0 then round((il.list_price - il.unit_price) * 100.0 / il.list_price, 2) else 0 end as "DiscountPercent"
              from sales.invoice_lines il
              join sales.invoices i on i.company_id = il.company_id and i.id = il.invoice_id
              join catalog.variants v on v.company_id = il.company_id and v.id = il.variant_id
              join catalog.products p on p.company_id = il.company_id and p.id = il.product_id
              left join master.units u on u.company_id = p.company_id and u.id = p.unit_id
              left join lateral (
                    select sum(-rl.qty) as returned_quantity
                      from sales.invoice_lines rl
                      join sales.invoices ri on ri.company_id = rl.company_id and ri.id = rl.invoice_id
                     where rl.company_id = il.company_id and rl.original_line_id = il.id and rl.original_line_sale_date = il.sale_date
                       and ri.status = 'POSTED') r on true
             where il.company_id = {companyId} and il.doc_type = 'SALE' and i.status = 'POSTED'
               and ((not {useLineIds} and il.invoice_id = {invoiceId}) or ({useLineIds} and il.id = any({lineIds})))
             order by i.invoice_no, il.line_no
            """).ToListAsync(cancellationToken);

        return rows.Select(row => new ReturnableSaleLineResponse(
            row.LineId, row.LineNumber, row.InvoiceId, row.InvoiceNumber, row.VariantId, row.ProductCode, row.ProductName, row.ProductNameUrdu, row.UnitName,
            row.SizeCode, row.Barcode, row.SoldQuantity, row.AlreadyReturnedQuantity, row.ReturnableQuantity, row.ListPrice, row.UnitPrice,
            row.DiscountPerUnit, row.DiscountPercent)).ToList();
    }

    // ------------------------------------------------------------------ lookups

    public async Task<IReadOnlyList<SaleCustomerLookupResponse>> SearchCustomersAsync(string? searchText, int limit, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var rows = await databaseContext.Database.SqlQuery<CustomerRow>($"""
            select p.id as "CustomerId", p.code as "Code", p.name as "Name", p.phone as "PhoneNumber",
                   cast(coalesce(b.balance, 0) as bigint) as "BalanceAmount"
              from contacts.customers p
              left join contacts.customer_balances b on b.company_id = p.company_id and b.customer_id = p.id
             where p.company_id = {companyId} and not p.is_deleted and p.status = 'ACTIVE' and p.code <> 'WALKIN'
               and (cast({searchText} as text) is null
                    or position(lower(cast({searchText} as text)) in lower(p.name)) > 0
                    or position(lower(cast({searchText} as text)) in lower(p.code)) > 0
                    or position(lower(cast({searchText} as text)) in lower(coalesce(p.phone, ''))) > 0)
             order by p.name
             limit {limit}
            """).ToListAsync(cancellationToken);
        return rows.Select(row => new SaleCustomerLookupResponse(row.CustomerId, row.Code, row.Name, row.PhoneNumber, row.BalanceAmount)).ToList();
    }

    public async Task<IReadOnlyList<SaleSalesmanLookupResponse>> ListSalesmenAsync(Guid storeId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var rows = await databaseContext.Database.SqlQuery<SalesmanRow>($"""
            select p.id as "StaffId", p.code as "Code", p.name as "Name", p.is_manager as "IsManager"
              from contacts.staff p
             where p.company_id = {companyId} and not p.is_deleted and p.status = 'ACTIVE'
               and p.current_store_id = {storeId} and p.left_on is null
             order by p.name
            """).ToListAsync(cancellationToken);
        return rows.Select(row => new SaleSalesmanLookupResponse(row.StaffId, row.Code, row.Name, row.IsManager)).ToList();
    }

    public async Task<IReadOnlyList<SalePaymentAccountResponse>> ListPaymentAccountsAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var rows = await databaseContext.Database.SqlQuery<PaymentAccountRow>($"""
            select a.id as "AccountId", a.code as "Code", a.name as "Title",
                   case when a.system_role = 'BANK' or a.group_code = '102' then 'BANK' else 'CASH' end as "KindCode", a.store_id as "StoreId"
              from acct.accounts a
             where a.company_id = {companyId} and a.is_active and a.is_postable and not a.is_subledger_control
               and (a.system_role in ('CASH', 'BANK') or a.group_code in ('101', '102'))
             order by case when a.system_role = 'CASH' then 0 else 1 end, a.code
            """).ToListAsync(cancellationToken);
        return rows.Select(row => new SalePaymentAccountResponse(row.AccountId, row.Code, row.Title, row.KindCode, row.StoreId)).ToList();
    }
        
    public async Task<IReadOnlyList<SalePosTerminalResponse>> ListPosTerminalsAsync(Guid storeId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var rows = await databaseContext.Database.SqlQuery<PosTerminalRow>($"""
            select d.id as "DeviceId", coalesce(d.label, 'Terminal') as "Label"
              from core.devices d
             where d.company_id = {companyId} and d.store_id = {storeId} and d.is_active
             order by d.label, d.created_at
            """).ToListAsync(cancellationToken);
        return rows.Select(row => new SalePosTerminalResponse(row.DeviceId, row.Label)).ToList();
    }

    // ------------------------------------------------------------------ counters

    public async Task<SaleStoreCountersResponse> GetStoreCountersAsync(Guid storeId, DateOnly businessDate, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var invoiceSeries = DocumentNumberSeriesCodes.SaleInvoice;
        var returnSeries = DocumentNumberSeriesCodes.SaleReturnInvoice;
        var dailyBillSeries = SaleNumberSeriesCodes.DailyBill;
        var saleType = SaleDocumentTypeCodes.Sale;
        var postedStatus = SaleInvoiceStatusCodes.Posted;

        // The next numbers are only read here, never taken: the number is given when the invoice is posted.
        var row = await databaseContext.Database.SqlQuery<CounterRow>($"""
            select s.name as "StoreName",
                   coalesce((select n.next_value from core.number_series n where n.company_id = s.company_id and n.store_id = s.id
                                and n.series_code = {invoiceSeries} and n.period_key = date '1900-01-01'), 1) as "NextInvoiceNumber",
                   coalesce((select n.next_value from core.number_series n where n.company_id = s.company_id and n.store_id = s.id
                                and n.series_code = {dailyBillSeries} and n.period_key = {businessDate}), 1) as "NextDailyBillNumber",
                   coalesce((select n.next_value from core.number_series n where n.company_id = s.company_id and n.store_id = s.id
                                and n.series_code = {returnSeries} and n.period_key = date '1900-01-01'), 1) as "NextReturnInvoiceNumber",
                   cast(coalesce((select sum(i.net_amount) from sales.invoices i where i.company_id = s.company_id and i.store_id = s.id
                                and i.sale_date = {businessDate} and i.doc_type = {saleType} and i.status = {postedStatus}), 0) as bigint) as "TodaySaleAmount",
                   c.rounding_mode as "RoundingModeCode"
              from core.stores s
              join platform.companies c on c.id = s.company_id
             where s.company_id = {companyId} and s.id = {storeId}
            """).SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "The store was not found.");
        return new SaleStoreCountersResponse(
            row.StoreName, row.NextInvoiceNumber, row.NextDailyBillNumber, row.NextReturnInvoiceNumber, row.TodaySaleAmount, row.RoundingModeCode);
    }

    // ------------------------------------------------------------------ row classes (property names match the quoted column aliases)

    private sealed class InvoiceHeaderRow
    {
        public Guid Id { get; set; }
        public Guid StoreId { get; set; }
        public string StoreName { get; set; } = "";
        public string DocumentTypeCode { get; set; } = "";
        public long InvoiceNumber { get; set; }
        public int? DailyBillNumber { get; set; }
        public DateOnly SaleDate { get; set; }
        public DateTimeOffset OccurredAt { get; set; }
        public Guid? CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string? WalkInCustomerName { get; set; }
        public string? WalkInCustomerMobile { get; set; }
        public Guid? SalesmanStaffId { get; set; }
        public string? SalesmanName { get; set; }
        public string? PosTerminalLabel { get; set; }
        public string? Description { get; set; }
        public long GrossAmount { get; set; }
        public long DiscountAmount { get; set; }
        public long TaxAmount { get; set; }
        public long NetAmount { get; set; }
        public long PaidAmount { get; set; }
        public long CashReturnedAmount { get; set; }
        public long OutstandingAmount { get; set; }
        public string StatusCode { get; set; } = "";
        public string? VoidReason { get; set; }
        public DateTimeOffset? VoidedAt { get; set; }
        public string? VoidedByName { get; set; }
        public string? CreatedByName { get; set; }
        public Guid? OriginalInvoiceId { get; set; }
        public long? OriginalInvoiceNumber { get; set; }
        public Guid? RelatedInvoiceId { get; set; }
        public long? RelatedInvoiceNumber { get; set; }
        public Guid? ReplacesInvoiceId { get; set; }
        public long? ReplacesInvoiceNumber { get; set; }
        public long RowVersion { get; set; }
    }

    private sealed class InvoiceLineRow
    {
        public Guid LineId { get; set; }
        public int LineNumber { get; set; }
        public Guid VariantId { get; set; }
        public Guid ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string? ProductNameUrdu { get; set; }
        public string? UnitName { get; set; }
        public string SizeCode { get; set; } = "";
        public string Barcode { get; set; } = "";
        public decimal Quantity { get; set; }
        public long ListPrice { get; set; }
        public long UnitPrice { get; set; }
        public long DiscountPerUnit { get; set; }
        public decimal DiscountPercent { get; set; }
        public long DiscountAmount { get; set; }
        public long NetAmount { get; set; }
        public string DiscountSourceCode { get; set; } = "";
        public Guid? OriginalLineId { get; set; }
        public long? OriginalInvoiceNumber { get; set; }
        public string? ReturnReason { get; set; }
        public string? ItemConditionCode { get; set; }
    }

    private sealed class CustomerPurchaseLineRow
    {
        public Guid InvoiceId { get; set; }
        public Guid LineId { get; set; }
        public string ProductName { get; set; } = "";
        public string SizeCode { get; set; } = "";
        public string? ColorName { get; set; }
        public decimal Quantity { get; set; }
        public long UnitPrice { get; set; }
        public long NetAmount { get; set; }
    }

    private sealed class InvoicePaymentRow
    {
        public Guid Id { get; set; }
        public string PaymentMethodCode { get; set; } = "";
        public Guid PaymentAccountId { get; set; }
        public string PaymentAccountTitle { get; set; } = "";
        public long Amount { get; set; }
        public string? ReferenceNumber { get; set; }
    }

    private sealed class InvoiceListRow
    {
        public Guid Id { get; set; }
        public Guid StoreId { get; set; }
        public string StoreName { get; set; } = "";
        public string DocumentTypeCode { get; set; } = "";
        public long InvoiceNumber { get; set; }
        public int? DailyBillNumber { get; set; }
        public DateOnly SaleDate { get; set; }
        public string? CustomerName { get; set; }
        public string? WalkInCustomerName { get; set; }
        public string? SalesmanName { get; set; }
        public long NetAmount { get; set; }
        public long PaidAmount { get; set; }
        public long OutstandingAmount { get; set; }
        public string StatusCode { get; set; } = "";
        public bool IsPartOfExchange { get; set; }
        public string? VoidReason { get; set; }
        public DateTimeOffset? VoidedAt { get; set; }
        public string? VoidedByName { get; set; }
    }

    private sealed class SaleItemRow
    {
        public Guid VariantId { get; set; }
        public Guid ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string? ProductNameUrdu { get; set; }
        public string? UnitName { get; set; }
        public string SizeCode { get; set; } = "";
        public string? ColorName { get; set; }
        public string Barcode { get; set; } = "";
        public decimal StockQuantity { get; set; }
        public long ListPrice { get; set; }
        public long SuggestedUnitPrice { get; set; }
        public long PromotionDiscountPerUnit { get; set; }
        public bool HasPromotionConflict { get; set; }
        public decimal TaxRatePercentage { get; set; }
        public bool HasPicture { get; set; }
    }

    private sealed class ReturnableHeaderRow
    {
        public Guid InvoiceId { get; set; }
        public long InvoiceNumber { get; set; }
        public Guid StoreId { get; set; }
        public DateOnly SaleDate { get; set; }
        public Guid? CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string? WalkInCustomerName { get; set; }
    }

    private sealed class ReturnableLineRow
    {
        public Guid LineId { get; set; }
        public int LineNumber { get; set; }
        public Guid InvoiceId { get; set; }
        public long InvoiceNumber { get; set; }
        public Guid VariantId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string? ProductNameUrdu { get; set; }
        public string? UnitName { get; set; }
        public string SizeCode { get; set; } = "";
        public string Barcode { get; set; } = "";
        public decimal SoldQuantity { get; set; }
        public decimal AlreadyReturnedQuantity { get; set; }
        public decimal ReturnableQuantity { get; set; }
        public long ListPrice { get; set; }
        public long UnitPrice { get; set; }
        public long DiscountPerUnit { get; set; }
        public decimal DiscountPercent { get; set; }
    }

    private sealed class CustomerRow
    {
        public Guid CustomerId { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? PhoneNumber { get; set; }
        public long BalanceAmount { get; set; }
    }

    private sealed class SalesmanRow
    {
        public Guid StaffId { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsManager { get; set; }
    }

    private sealed class PaymentAccountRow
    {
        public Guid AccountId { get; set; }
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string KindCode { get; set; } = "";
        public Guid StoreId { get; set; }
    }

    private sealed class PosTerminalRow
    {
        public Guid DeviceId { get; set; }
        public string Label { get; set; } = "";
    }

    private sealed class CounterRow
    {
        public string StoreName { get; set; } = "";
        public long NextInvoiceNumber { get; set; }
        public long NextDailyBillNumber { get; set; }
        public long NextReturnInvoiceNumber { get; set; }
        public long TodaySaleAmount { get; set; }
        public string RoundingModeCode { get; set; } = "FLOOR";
    }
}
