using EFootwear.Application.Abstractions;
using EFootwear.Infrastructure.Persistence;

namespace EFootwear.Infrastructure.Repositories;

public sealed class FinanceRepository(Db db) : IFinanceRepository
{
    // ------------------------------------------------------------------------------------------ accounts and ledgers

    public Task<List<Row>> AccountsAsync(bool includeInactive, CancellationToken ct)
        => db.RowsAsync(
            @"select a.id, a.code, a.main_type, a.group_code, a.name, a.parent_id, a.store_id, s.name as store, a.system_role, a.is_postable, a.is_party_control, a.is_active, a.row_ver,
                     coalesce(b.debit, 0) as debit, coalesce(b.credit, 0) as credit, coalesce(b.debit, 0) - coalesce(b.credit, 0) as balance
                from acct.accounts a join core.stores s on s.company_id = a.company_id and s.id = a.store_id
                left join (select account_id, sum(debit) as debit, sum(credit) as credit from acct.account_month_balances group by account_id) b on b.account_id = a.id
               where @inactive or a.is_active order by a.code", new { inactive = includeInactive }, ct);

    public Task<Row?> AccountAsync(long id, CancellationToken ct)
        => db.RowAsync("select id, code, main_type, group_code, name, parent_id, store_id, system_role, is_postable, is_party_control, is_active, row_ver from acct.accounts where id = @id", new { id }, ct);

    public Task<long> InsertAccountAsync(long companyId, string code, string mainType, string group, string name, long? parentId, long storeId, bool postable, CancellationToken ct)
        => db.ScalarAsync<long>("insert into acct.accounts (company_id, code, main_type, group_code, name, parent_id, store_id, is_postable, created_by) values (@c, @code, @t, @g, @n, @p, @s, @pp, core.current_user_id()) returning id",
            new { c = companyId, code, t = mainType, g = group, n = name, p = parentId, s = storeId, pp = postable }, ct);

    public async Task<bool> UpdateAccountAsync(long id, string name, bool isActive, long? rowVer, CancellationToken ct)
        => await db.ExecAsync("update acct.accounts set name = @n, is_active = @a where id = @id and (@rv::bigint is null or row_ver = @rv)", new { id, n = name, a = isActive, rv = rowVer }, ct) == 1;

    public Task<long> RoleAccountAsync(long storeId, string role, CancellationToken ct)
        => db.ScalarAsync<long>("select id from acct.accounts where store_id = @s and system_role = @r and is_active", new { s = storeId, r = role }, ct);

    /// <summary>All postings on the party's account in the period, with a running balance (positive = they owe us).</summary>
    public Task<List<Row>> PartyLedgerAsync(long partyId, DateOnly from, DateOnly to, CancellationToken ct)
        => db.RowsAsync(
            @"select l.entry_date, l.entry_id, e.source_type, e.source_id, coalesce(l.description, e.description) as description, l.store_id, l.debit, l.credit,
                     (select coalesce(sum(x.debit - x.credit), 0) from acct.journal_lines x where x.party_id = @p and x.entry_date < @f)
                       + sum(l.debit - l.credit) over (order by l.entry_date, l.entry_id, l.line_no) as running
                from acct.journal_lines l join acct.journal_entries e on e.company_id = l.company_id and e.id = l.entry_id and e.entry_date = l.entry_date
               where l.party_id = @p and l.entry_date between @f and @t order by l.entry_date, l.entry_id, l.line_no", new { p = partyId, f = from, t = to }, ct);

    public async Task<long> PartyOpeningAsync(long partyId, DateOnly before, CancellationToken ct)
        => await db.ScalarAsync<long?>("select sum(debit - credit) from acct.journal_lines where party_id = @p and entry_date < @d", new { p = partyId, d = before }, ct) ?? 0;

    public Task<long> NextNumberAsync(long companyId, long storeId, string series, CancellationToken ct)
        => db.ScalarAsync<long>("select core.next_number(@c, @s, @series)", new { c = companyId, s = storeId, series }, ct);

    public Task<long> InsertJournalAsync(long companyId, DateOnly date, long storeId, string sourceType, long? sourceId, string description, long userId, CancellationToken ct)
        => db.ScalarAsync<long>("insert into acct.journal_entries (company_id, entry_date, store_id, source_type, source_id, description, posted_by) values (@c, @d, @s, @t, @i, @desc, @u) returning id",
            new { c = companyId, d = date, s = storeId, t = sourceType, i = sourceId, desc = description, u = userId }, ct);

    public Task AddLineAsync(long companyId, long entryId, DateOnly date, int lineNo, long storeId, long accountId, long? partyId, long debit, long credit, string? description, CancellationToken ct)
        => db.ExecAsync("select acct.add_line(@c, @e, @d, @n, @s, @a, @p, @dr, @cr, @desc)", new { c = companyId, e = entryId, d = date, n = lineNo, s = storeId, a = accountId, p = partyId, dr = debit, cr = credit, desc = description }, ct);

    public Task<long> ReverseEntryAsync(long companyId, long entryId, string sourceType, long sourceId, string description, CancellationToken ct)
        => db.ScalarAsync<long>("select acct.reverse_entry(@c, @e, @t, @i, @d)", new { c = companyId, e = entryId, t = sourceType, i = sourceId, d = description }, ct);

    // ------------------------------------------------------------------------------------------ vouchers

    public Task<long> InsertVoucherAsync(VoucherInsert v, CancellationToken ct)
        => db.ScalarAsync<long>(
            @"insert into acct.vouchers (company_id, store_id, voucher_type, voucher_no, voucher_date, debit_account_id, credit_account_id, amount, party_id, payment_mode, ref_no, description, journal_entry_id, created_by)
              values (@c, @s, @t, @n, @d, @da, @ca, @a, @p, @m, @r, @desc, @je, core.current_user_id()) returning id",
            new { c = v.CompanyId, s = v.StoreId, t = v.Type, n = v.VoucherNo, d = v.Date, da = v.DebitAccountId, ca = v.CreditAccountId, a = v.Amount, p = v.PartyId, m = v.Mode, r = v.RefNo, desc = v.Description, je = v.JournalEntryId }, ct);

    private const string VoucherSelect = @"select v.id, v.store_id, s.name as store, v.voucher_type, v.voucher_no, v.voucher_date, v.debit_account_id, da.code as debit_code, da.name as debit_name,
            v.credit_account_id, ca.code as credit_code, ca.name as credit_name, v.amount, v.party_id, p.name as party, v.payment_mode, v.ref_no, v.description, v.journal_entry_id, v.status, v.void_reason, v.created_at, v.row_ver
        from acct.vouchers v join core.stores s on s.company_id = v.company_id and s.id = v.store_id
        join acct.accounts da on da.company_id = v.company_id and da.id = v.debit_account_id join acct.accounts ca on ca.company_id = v.company_id and ca.id = v.credit_account_id
        left join party.parties p on p.company_id = v.company_id and p.id = v.party_id";

    public Task<Row?> VoucherAsync(long id, CancellationToken ct) => db.RowAsync(VoucherSelect + " where v.id = @id", new { id }, ct);

    public Task<List<Row>> VoucherAllocationsAsync(long voucherId, CancellationToken ct)
        => db.RowsAsync("select id, purchase_invoice_id, sale_invoice_id, expense_id, payroll_line_id, amount from acct.voucher_allocations where voucher_id = @v order by id", new { v = voucherId }, ct);

    public Task<Page<Row>> VouchersAsync(VoucherFilter f, PageQuery q, CancellationToken ct)
        => db.PageAsync(VoucherSelect + @" where (@st::bigint is null or v.store_id = @st) and (@t::text is null or v.voucher_type = @t) and (@f::date is null or v.voucher_date >= @f) and (@to::date is null or v.voucher_date <= @to)
                 and (@p::bigint is null or v.party_id = @p) and (@s::text is null or v.status = @s) and (@q::text is null or p.name ilike @q or v.ref_no ilike @q or v.description ilike @q)",
            "v.voucher_date desc, v.id desc", new { st = f.StoreId, t = f.Type, f = f.From, to = f.To, p = f.PartyId, s = f.Status, q = CatalogRepository.Like(q.Search) }, q, ct);

    public Task InsertAllocationAsync(long companyId, long voucherId, long storeId, long? purchaseInvoiceId, long? saleInvoiceId, long? expenseId, long amount, CancellationToken ct)
        => db.ExecAsync("insert into acct.voucher_allocations (company_id, voucher_id, store_id, purchase_invoice_id, sale_invoice_id, expense_id, amount) values (@c, @v, @s, @pi, @si, @e, @a)",
            new { c = companyId, v = voucherId, s = storeId, pi = purchaseInvoiceId, si = saleInvoiceId, e = expenseId, a = amount }, ct);

    public Task DeleteAllocationsAsync(long voucherId, CancellationToken ct) => db.ExecAsync("delete from acct.voucher_allocations where voucher_id = @v", new { v = voucherId }, ct);

    public Task VoidVoucherAsync(long id, string reason, long userId, CancellationToken ct)
        => db.ExecAsync("update acct.vouchers set status = 'VOID', void_reason = @r, voided_by = @u, voided_at = now() where id = @id and status = 'POSTED'", new { id, r = reason, u = userId }, ct);

    public Task<Row?> PurchaseBalanceAsync(long invoiceId, CancellationToken ct)
        => db.RowAsync("select invoice_id, vendor_party_id, store_id, net_amount, paid_amount, remaining_amount from purch.v_invoice_balance where invoice_id = @i", new { i = invoiceId }, ct);

    public Task<List<Row>> OpenPurchaseBillsAsync(long vendorId, CancellationToken ct)
        => db.RowsAsync("select b.invoice_id, i.invoice_no, i.vendor_bill_no, b.bill_date, b.due_date, b.net_amount, b.paid_amount, b.remaining_amount from purch.v_invoice_balance b join purch.invoices i on i.company_id = b.company_id and i.id = b.invoice_id where b.vendor_party_id = @v and b.remaining_amount > 0 order by b.bill_date, b.invoice_id", new { v = vendorId }, ct);

    public Task<List<Row>> VendorSummaryAsync(CancellationToken ct) => db.RowsAsync("select * from purch.v_vendor_summary where payable_balance <> 0 or open_bill_remaining > 0 order by payable_balance desc", null, ct);
    public Task<List<Row>> VendorDueAsync(CancellationToken ct) => db.RowsAsync("select schedule_id, store_id, vendor_party_id, vendor_name, due_date, amount_due, due_status, days_overdue from purch.v_vendor_due order by due_date", null, ct);

    public Task<long> InsertScheduleAsync(long companyId, long storeId, long vendorId, long? invoiceId, DateOnly due, long amount, string? note, CancellationToken ct)
        => db.ScalarAsync<long>("insert into purch.vendor_payment_schedule (company_id, store_id, vendor_party_id, purchase_invoice_id, due_date, planned_amount, note, created_by) values (@c, @s, @v, @i, @d, @a, @n, core.current_user_id()) returning id",
            new { c = companyId, s = storeId, v = vendorId, i = invoiceId, d = due, a = amount, n = note }, ct);

    public async Task<bool> CancelScheduleAsync(long id, CancellationToken ct)
        => await db.ExecAsync("update purch.vendor_payment_schedule set status = 'CANCELLED' where id = @id and status in ('OPEN','PARTIAL')", new { id }, ct) == 1;

    public Task<Row?> ExpenseForPaymentAsync(long id, CancellationToken ct)
        => db.RowAsync("select id, store_id, amount, paid_amount, status from exp.expenses where id = @id", new { id }, ct);

    public Task SetExpensePaidAsync(long id, long paid, string status, CancellationToken ct)
        => db.ExecAsync("update exp.expenses set paid_amount = @p, payment_status = @s where id = @id", new { id, p = paid, s = status }, ct);

    // ------------------------------------------------------------------------------------------ expenses

    public Task<List<Row>> ExpenseCategoriesAsync(CancellationToken ct)
        => db.RowsAsync("select c.id, c.code, c.name, c.gl_account_id, a.code as gl_code, c.is_recurring, c.is_active from exp.expense_categories c join acct.accounts a on a.company_id = c.company_id and a.id = c.gl_account_id order by c.name", null, ct);

    public Task<long> InsertExpenseCategoryAsync(long companyId, string code, string name, long glAccountId, bool recurring, CancellationToken ct)
        => db.ScalarAsync<long>("insert into exp.expense_categories (company_id, code, name, gl_account_id, is_recurring, created_by) values (@c, @code, @n, @g, @r, core.current_user_id()) returning id",
            new { c = companyId, code, n = name, g = glAccountId, r = recurring }, ct);

    public Task<long> InsertExpenseAsync(ExpenseInsert e, CancellationToken ct)
        => db.ScalarAsync<long>(
            @"insert into exp.expenses (company_id, store_id, category_id, expense_date, period_month, amount, payee_party_id, bill_ref, description, due_date, payment_method, created_by)
              values (@c, @s, @cat, @d, date_trunc('month', @d::date)::date, @a, @p, @b, @desc, @due, @m, core.current_user_id()) returning id",
            new { c = e.CompanyId, s = e.StoreId, cat = e.CategoryId, d = e.Date, a = e.Amount, p = e.PayeeId, b = e.BillRef, desc = e.Description, due = e.DueDate, m = e.Method }, ct);

    public async Task<bool> UpdateExpenseAsync(long id, ExpenseInsert e, long? rowVer, CancellationToken ct)
        => await db.ExecAsync(
            @"update exp.expenses set category_id = @cat, expense_date = @d, period_month = date_trunc('month', @d::date)::date, amount = @a, payee_party_id = @p, bill_ref = @b, description = @desc,
                     due_date = @due, payment_method = @m, status = case when status = 'REJECTED' then 'DRAFT' else status end where id = @id and status in ('DRAFT','REJECTED') and (@rv::bigint is null or row_ver = @rv)",
            new { id, cat = e.CategoryId, d = e.Date, a = e.Amount, p = e.PayeeId, b = e.BillRef, desc = e.Description, due = e.DueDate, m = e.Method, rv = rowVer }, ct) == 1;

    public Task<Row?> ExpenseAsync(long id, CancellationToken ct)
        => db.RowAsync(
            @"select e.id, e.store_id, s.name as store, e.category_id, c.name as category, e.expense_date, e.period_month, e.amount, e.payee_party_id, p.name as payee, e.bill_ref, e.description, e.due_date,
                     e.payment_method, e.paid_amount, e.payment_status, e.status, e.submitted_by, e.submitted_at, e.approved_by, e.approved_at, e.rejected_reason, e.journal_entry_id, e.created_by, e.created_at, e.row_ver
                from exp.expenses e join core.stores s on s.company_id = e.company_id and s.id = e.store_id join exp.expense_categories c on c.company_id = e.company_id and c.id = e.category_id
                left join party.parties p on p.company_id = e.company_id and p.id = e.payee_party_id where e.id = @id", new { id }, ct);

    public Task<List<Row>> ExpenseAllocationsAsync(long id, CancellationToken ct)
        => db.RowsAsync("select a.store_id, s.name as store, a.amount from exp.expense_allocations a join core.stores s on s.company_id = a.company_id and s.id = a.store_id where a.expense_id = @e order by a.store_id", new { e = id }, ct);

    public async Task ReplaceExpenseAllocationsAsync(long companyId, long id, IReadOnlyCollection<(long StoreId, long Amount)> allocations, CancellationToken ct)
    {
        await db.ExecAsync("delete from exp.expense_allocations where company_id = @c and expense_id = @e", new { c = companyId, e = id }, ct);
        foreach (var (s, a) in allocations)
            await db.ExecAsync("insert into exp.expense_allocations (company_id, expense_id, store_id, amount) values (@c, @e, @s, @a)", new { c = companyId, e = id, s, a }, ct);
    }

    public Task<Page<Row>> ExpensesAsync(ExpenseFilter f, PageQuery q, CancellationToken ct)
        => db.PageAsync(
            @"select e.id, e.store_id, s.name as store, e.category_id, c.name as category, e.expense_date, e.amount, p.name as payee, e.bill_ref, e.description, e.due_date, e.paid_amount, e.payment_status, e.status
                from exp.expenses e join core.stores s on s.company_id = e.company_id and s.id = e.store_id join exp.expense_categories c on c.company_id = e.company_id and c.id = e.category_id
                left join party.parties p on p.company_id = e.company_id and p.id = e.payee_party_id
               where (@st::bigint is null or e.store_id = @st) and (@s::text is null or e.status = @s) and (@ps::text is null or e.payment_status = @ps) and (@cat::bigint is null or e.category_id = @cat)
                 and (@f::date is null or e.expense_date >= @f) and (@t::date is null or e.expense_date <= @t) and (@q::text is null or e.description ilike @q or e.bill_ref ilike @q or p.name ilike @q)",
            "e.expense_date desc, e.id desc", new { st = f.StoreId, s = f.Status, ps = f.PaymentStatus, cat = f.CategoryId, f = f.From, t = f.To, q = CatalogRepository.Like(q.Search) }, q, ct);

    public Task SubmitExpenseAsync(long companyId, long id, long userId, CancellationToken ct) => db.ExecAsync("select exp.submit_expense(@c, @i, @u)", new { c = companyId, i = id, u = userId }, ct);
    public Task<long> ApproveExpenseAsync(long companyId, long id, long userId, CancellationToken ct) => db.ScalarAsync<long>("select exp.approve_expense(@c, @i, @u)", new { c = companyId, i = id, u = userId }, ct);
    public Task RejectExpenseAsync(long companyId, long id, long userId, string reason, CancellationToken ct) => db.ExecAsync("select exp.reject_expense(@c, @i, @u, @r)", new { c = companyId, i = id, u = userId, r = reason }, ct);
    public async Task<bool> DeleteDraftExpenseAsync(long id, CancellationToken ct) => await db.ExecAsync("update exp.expenses set status = 'VOID' where id = @id and status in ('DRAFT','REJECTED')", new { id }, ct) == 1;

    // ------------------------------------------------------------------------------------------ commissions and payroll

    public Task<List<Row>> PlansAsync(CancellationToken ct)
        => db.RowsAsync("select id, name, applies_to, basis, calc_type, default_rate, default_fixed, min_store_sales, store_id, valid_from, valid_to, is_active, row_ver, (select count(*) from hr.commission_plan_members m where m.company_id = p.company_id and m.plan_id = p.id) as member_count from hr.commission_plans p where not is_deleted order by name", null, ct);

    public async Task<Row?> PlanAsync(long id, CancellationToken ct)
    {
        var p = await db.RowAsync("select id, name, applies_to, basis, calc_type, default_rate, default_fixed, min_store_sales, store_id, valid_from, valid_to, is_active, row_ver from hr.commission_plans where id = @id and not is_deleted", new { id }, ct);
        if (p is null) return null;
        p["members"] = await db.RowsAsync("select m.employee_party_id, pa.name from hr.commission_plan_members m join party.parties pa on pa.company_id = m.company_id and pa.id = m.employee_party_id where m.plan_id = @id order by pa.name", new { id }, ct);
        p["rules"] = await db.RowsAsync("select id, rule_scope, product_id, category_id, price_band_id, calc_type, rate, fixed_amount, min_line_amount, priority, valid_from, valid_to from hr.commission_rules where plan_id = @id order by priority desc, id", new { id }, ct);
        return p;
    }

    public Task<long> InsertPlanAsync(long companyId, PlanInput p, CancellationToken ct)
        => db.ScalarAsync<long>(
            @"insert into hr.commission_plans (company_id, name, applies_to, basis, calc_type, default_rate, default_fixed, min_store_sales, store_id, valid_from, valid_to, is_active, created_by)
              values (@c, @n, @at, @b, @ct, @dr, @df, @ms, @st, @vf, @vt, @a, core.current_user_id()) returning id", PlanArgs(companyId, p), ct);

    public async Task<bool> UpdatePlanAsync(long id, PlanInput p, long? rowVer, CancellationToken ct)
        => await db.ExecAsync(
            @"update hr.commission_plans set name = @n, applies_to = @at, basis = @b, calc_type = @ct, default_rate = @dr, default_fixed = @df, min_store_sales = @ms, store_id = @st, valid_from = @vf, valid_to = @vt, is_active = @a
               where id = @id and not is_deleted and (@rv::bigint is null or row_ver = @rv)", PlanArgs(0, p, id, rowVer), ct) == 1;

    public async Task ReplacePlanMembersAsync(long companyId, long planId, IReadOnlyCollection<long> employees, CancellationToken ct)
    {
        await db.ExecAsync("delete from hr.commission_plan_members where company_id = @c and plan_id = @p", new { c = companyId, p = planId }, ct);
        foreach (var e in employees) await db.ExecAsync("insert into hr.commission_plan_members (company_id, plan_id, employee_party_id) values (@c, @p, @e)", new { c = companyId, p = planId, e }, ct);
    }

    public Task<long> InsertCommissionRuleAsync(long companyId, long planId, CommissionRuleInput r, CancellationToken ct)
        => db.ScalarAsync<long>(
            @"insert into hr.commission_rules (company_id, plan_id, rule_scope, product_id, category_id, price_band_id, calc_type, rate, fixed_amount, min_line_amount, priority, valid_from, valid_to, created_by)
              values (@c, @p, @s, @pr, @ca, @pb, @ct, @r, @f, @m, @pri, @vf, @vt, core.current_user_id()) returning id",
            new { c = companyId, p = planId, s = r.Scope, pr = r.ProductId, ca = r.CategoryId, pb = r.PriceBandId, ct = r.CalcType, r = r.Rate, f = r.Fixed, m = r.MinLineAmount, pri = r.Priority, vf = r.ValidFrom, vt = r.ValidTo }, ct);

    public Task EndCommissionRuleAsync(long ruleId, DateOnly validTo, CancellationToken ct) => db.ExecAsync("update hr.commission_rules set valid_to = @t where id = @id", new { id = ruleId, t = validTo }, ct);

    public Task<Page<Row>> CommissionEntriesAsync(long? storeId, long? employeeId, string? status, DateOnly? from, DateOnly? to, PageQuery q, CancellationToken ct)
        => db.PageAsync(
            @"select e.id, e.store_id, e.sale_date, e.invoice_id, e.employee_party_id, p.name as employee, e.plan_id, e.entry_type, e.calc_type, e.rate, e.fixed_amount, e.qty, e.base_amount, e.commission_amount, e.status, e.approved_at, e.payroll_line_id
                from hr.commission_entries e join party.parties p on p.company_id = e.company_id and p.id = e.employee_party_id
               where (@st::bigint is null or e.store_id = @st) and (@emp::bigint is null or e.employee_party_id = @emp) and (@s::text is null or e.status = @s)
                 and (@f::date is null or e.sale_date >= @f) and (@t::date is null or e.sale_date <= @t)",
            "e.sale_date desc, e.id desc", new { st = storeId, emp = employeeId, s = status, f = from, t = to }, q, ct);

    public Task<List<Row>> CommissionSummaryAsync(long? storeId, DateOnly? month, CancellationToken ct)
        => db.RowsAsync("select s.store_id, s.employee_party_id, p.name as employee, s.period_month, s.eligible_base, s.earned, s.pending, s.approved, s.paid from hr.v_commission_summary s join party.parties p on p.company_id = s.company_id and p.id = s.employee_party_id where (@st::bigint is null or s.store_id = @st) and (@m::date is null or s.period_month = @m) order by s.period_month desc, p.name",
            new { st = storeId, m = month }, ct);

    public Task<int> ApproveCommissionsAsync(long companyId, long storeId, DateOnly from, DateOnly to, long userId, CancellationToken ct)
        => db.ScalarAsync<int>("select hr.approve_commissions(@c, @s, @f, @t, @u)", new { c = companyId, s = storeId, f = from, t = to, u = userId }, ct);

    public Task<long> BuildPayrollAsync(long companyId, long storeId, DateOnly month, long userId, CancellationToken ct)
        => db.ScalarAsync<long>("select hr.build_payroll_run(@c, @s, @m, @u)", new { c = companyId, s = storeId, m = month, u = userId }, ct);

    public Task<long> ApprovePayrollAsync(long companyId, long runId, long userId, CancellationToken ct)
        => db.ScalarAsync<long>("select hr.approve_payroll_run(@c, @r, @u)", new { c = companyId, r = runId, u = userId }, ct);

    public Task<List<Row>> PayrollRunsAsync(long? storeId, CancellationToken ct)
        => db.RowsAsync(
            @"select r.id, r.store_id, s.name as store, r.period_month, r.status, r.approved_at, r.journal_entry_id,
                     (select count(*) from hr.payroll_lines l where l.company_id = r.company_id and l.payroll_run_id = r.id and l.status <> 'CANCELLED') as employees,
                     (select coalesce(sum(l.net_pay), 0) from hr.payroll_lines l where l.company_id = r.company_id and l.payroll_run_id = r.id and l.status <> 'CANCELLED') as net_total,
                     (select coalesce(sum(l.outstanding_amount), 0) from hr.payroll_lines l where l.company_id = r.company_id and l.payroll_run_id = r.id and l.status <> 'CANCELLED') as outstanding
                from hr.payroll_runs r join core.stores s on s.company_id = r.company_id and s.id = r.store_id where (@st::bigint is null or r.store_id = @st) order by r.period_month desc, r.store_id", new { st = storeId }, ct);

    public Task<Row?> PayrollRunAsync(long id, CancellationToken ct)
        => db.RowAsync("select r.id, r.store_id, s.name as store, r.period_month, r.status, r.approved_by, r.approved_at, r.journal_entry_id, r.row_ver from hr.payroll_runs r join core.stores s on s.company_id = r.company_id and s.id = r.store_id where r.id = @id", new { id }, ct);

    public Task<List<Row>> PayrollLinesAsync(long runId, CancellationToken ct)
        => db.RowsAsync("select l.id, l.employee_party_id, p.name as employee, l.base_salary, l.total_sales, l.commission_amount, l.incentive_amount, l.deduction_amount, l.net_pay, l.paid_amount, l.outstanding_amount, l.status from hr.payroll_lines l join party.parties p on p.company_id = l.company_id and p.id = l.employee_party_id where l.payroll_run_id = @r and l.status <> 'CANCELLED' order by p.name", new { r = runId }, ct);

    public Task<Row?> PayrollLineAsync(long id, CancellationToken ct)
        => db.RowAsync("select id, payroll_run_id, store_id, employee_party_id, period_month, net_pay, paid_amount, outstanding_amount, status from hr.payroll_lines where id = @id", new { id }, ct);

    public Task<long> InsertAdjustmentAsync(long companyId, long employeeId, DateOnly month, string type, long amount, string? reason, CancellationToken ct)
        => db.ScalarAsync<long>("insert into hr.payroll_adjustments (company_id, employee_party_id, period_month, adjustment_type, amount, reason, created_by) values (@c, @e, date_trunc('month', @m::date)::date, @t, @a, @r, core.current_user_id()) returning id",
            new { c = companyId, e = employeeId, m = month, t = type, a = amount, r = reason }, ct);

    public Task<long> PaySalaryAsync(long companyId, long lineId, long amount, string method, DateOnly paidOn, long userId, string? reference, CancellationToken ct)
        => db.ScalarAsync<long>("select hr.pay_salary(@c, @l, @a, @m, @d, @u, @r)", new { c = companyId, l = lineId, a = amount, m = method, d = paidOn, u = userId, r = reference }, ct);

    public Task<List<Row>> SalaryPaymentsAsync(long lineId, CancellationToken ct)
        => db.RowsAsync("select id, paid_on, amount, method, reference, journal_entry_id, paid_by from hr.salary_payments where payroll_line_id = @l order by paid_on, id", new { l = lineId }, ct);

    public async Task<bool> CancelDraftRunAsync(long companyId, long id, CancellationToken ct)
    {
        var n = await db.ExecAsync("update hr.payroll_runs set status = 'CANCELLED' where company_id = @c and id = @id and status = 'DRAFT'", new { c = companyId, id }, ct);
        if (n == 1)
        {
            await db.ExecAsync("update hr.commission_entries set payroll_line_id = null where company_id = @c and payroll_line_id in (select id from hr.payroll_lines where company_id = @c and payroll_run_id = @id)", new { c = companyId, id }, ct);
            await db.ExecAsync("update hr.payroll_adjustments set payroll_line_id = null where company_id = @c and payroll_line_id in (select id from hr.payroll_lines where company_id = @c and payroll_run_id = @id)", new { c = companyId, id }, ct);
            await db.ExecAsync("update hr.payroll_lines set status = 'CANCELLED' where company_id = @c and payroll_run_id = @id", new { c = companyId, id }, ct);
        }
        return n == 1;
    }

    // ------------------------------------------------------------------------------------------ cash

    public Task<Page<Row>> PromisesAsync(long? storeId, long? customerId, string? status, PageQuery q, CancellationToken ct)
        => db.PageAsync("select p.id, p.store_id, p.customer_party_id, c.name as customer, p.promise_date, p.due_date, p.promised_amount, p.paid_amount, p.status, p.note from cash.promises p join party.parties c on c.company_id = p.company_id and c.id = p.customer_party_id where (@st::bigint is null or p.store_id = @st) and (@c::bigint is null or p.customer_party_id = @c) and (@s::text is null or p.status = @s)",
            "p.due_date, p.id", new { st = storeId, c = customerId, s = status }, q, ct);

    public Task<long> InsertPromiseAsync(long companyId, long storeId, long customerId, DateOnly due, long amount, string? note, CancellationToken ct)
        => db.ScalarAsync<long>("insert into cash.promises (company_id, store_id, customer_party_id, due_date, promised_amount, note, created_by) values (@c, @s, @cu, @d, @a, @n, core.current_user_id()) returning id", new { c = companyId, s = storeId, cu = customerId, d = due, a = amount, n = note }, ct);

    public Task<Row?> PromiseAsync(long id, CancellationToken ct) => db.RowAsync("select id, store_id, customer_party_id, promise_date, due_date, promised_amount, paid_amount, status, note from cash.promises where id = @id", new { id }, ct);
    public Task UpdatePromiseAsync(long id, long paid, string status, CancellationToken ct) => db.ExecAsync("update cash.promises set paid_amount = @p, status = @s where id = @id", new { id, p = paid, s = status }, ct);

    /// <summary>Cash the books say a store should hold on a date: its postings to the cash accounts (head-office cash with the store dimension, and the store's own cash account).</summary>
    public async Task<long> ExpectedCashAsync(long storeId, DateOnly asOf, CancellationToken ct)
        => await db.ScalarAsync<long?>(
            @"select sum(l.debit - l.credit) from acct.journal_lines l join acct.accounts a on a.company_id = l.company_id and a.id = l.account_id
               where a.system_role = 'CASH' and l.store_id = @s and l.entry_date <= @d", new { s = storeId, d = asOf }, ct) ?? 0;

    public Task<long?> LastCountedCashAsync(long storeId, DateOnly before, CancellationToken ct)
        => db.ScalarAsync<long?>("select counted_cash from cash.cash_counts where store_id = @s and count_date < @d order by count_date desc limit 1", new { s = storeId, d = before }, ct);

    public Task<long> InsertCashCountAsync(long companyId, long storeId, DateOnly date, long opening, long expected, long counted, string? note, long userId, CancellationToken ct)
        => db.ScalarAsync<long>("insert into cash.cash_counts (company_id, store_id, count_date, opening_cash, expected_cash, counted_cash, counted_by, note, created_by) values (@c, @s, @d, @o, @e, @cn, @u, @n, @u) returning id",
            new { c = companyId, s = storeId, d = date, o = opening, e = expected, cn = counted, n = note, u = userId }, ct);

    public Task InsertDenominationAsync(long companyId, long countId, int denomination, int notes, CancellationToken ct)
        => db.ExecAsync("insert into cash.count_denominations (company_id, count_id, denomination, note_count) values (@c, @i, @d, @n)", new { c = companyId, i = countId, d = denomination, n = notes }, ct);

    public async Task<Row?> CashCountAsync(long id, CancellationToken ct)
    {
        var c = await db.RowAsync("select id, store_id, count_date, opening_cash, expected_cash, counted_cash, difference, counted_by, note, created_at from cash.cash_counts where id = @id", new { id }, ct);
        if (c is null) return null;
        c["denominations"] = await db.RowsAsync("select denomination, note_count from cash.count_denominations where count_id = @id order by denomination desc", new { id }, ct);
        return c;
    }

    public Task<List<Row>> CashCountsAsync(long? storeId, DateOnly? from, DateOnly? to, CancellationToken ct)
        => db.RowsAsync("select id, store_id, count_date, opening_cash, expected_cash, counted_cash, difference, note from cash.cash_counts where (@s::bigint is null or store_id = @s) and (@f::date is null or count_date >= @f) and (@t::date is null or count_date <= @t) order by count_date desc, store_id", new { s = storeId, f = from, t = to }, ct);

    // ------------------------------------------------------------------------------------------ reports

    /// <summary>The report definitions. Each is a database function (which checks REPORT_VIEW / REPORT_ACCOUNTS itself) or a security-invoker view.</summary>
    private static readonly Dictionary<string, string> Sql = new()
    {
        ["account-ledger"] = "select out_entry_date as entry_date, out_entry_id as entry_id, out_source_type as source_type, out_description as description, out_party_id as party_id, out_debit as debit, out_credit as credit, out_running as running from rpt.account_ledger(@c, @acc, @f, @t, @st)",
        ["trial-balance"] = "select out_account_id as account_id, out_code as code, out_name as name, out_main_type as main_type, out_opening as opening, out_period_debit as period_debit, out_period_credit as period_credit, out_closing as closing from rpt.trial_balance(@c, @f, @t, @st)",
        ["account-balance"] = "select out_account_id as account_id, out_code as code, out_name as name, out_balance as balance from rpt.account_balance(@c, @t, @st)",
        ["general-journal"] = "select out_entry_id as entry_id, out_entry_date as entry_date, out_source_type as source_type, out_source_id as source_id, out_line_no as line_no, out_account_code as account_code, out_account_name as account_name, out_party_name as party, out_debit as debit, out_credit as credit, out_description as description from rpt.general_journal(@c, @f, @t, @st)",
        ["sale-register"] = "select * from rpt.sale_register(@c, @f, @t, @st, @dt, @cu)",
        ["sale-by-category"] = "select out_category_id as category_id, out_category as category, out_qty as qty, out_net as net, out_cost as cost, out_profit as profit from rpt.sale_by_category(@c, @f, @t, @st)",
        ["profit-summary"] = "select out_store_id as store_id, out_sales_net as sales_net, out_return_net as return_net, out_net_sales as net_sales, out_cost as cost, out_gross_profit as gross_profit, out_margin_pct as margin_pct from rpt.profit_summary(@c, @f, @t, @st)",
        ["purchase-register"] = "select * from rpt.purchase_register(@c, @f, @t, @st, @ve)",
        ["vendor-aging"] = "select out_vendor_id as vendor_id, out_vendor as vendor, out_not_due as not_due, out_days_1_30 as days_1_30, out_days_31_60 as days_31_60, out_days_61_90 as days_61_90, out_over_90 as over_90, out_total as total from rpt.vendor_aging(@c, @t)",
        ["stock-value"] = "select out_store_id as store_id, out_variant_id as variant_id, out_product_code as product_code, out_qty as qty, out_avg_cost as avg_cost, out_value as value from rpt.stock_value(@c, @st, @cat)",
        ["stock-adjustments"] = "select out_txn_date as txn_date, out_store_id as store_id, out_variant_id as variant_id, out_doc_type as doc_type, out_doc_id as doc_id, out_qty as qty, out_unit_cost as unit_cost, out_value as value from rpt.stock_adjustments(@c, @f, @t, @st)",
        ["commission-accrual"] = "select out_employee_id as employee_id, out_employee as employee, out_status as status, out_earned as earned, out_reversed as reversed, out_net as net, out_entries as entries from rpt.commission_accrual(@c, @f, @t, @st)",
        ["store-expenses"] = "select out_store_id as store_id, out_category_id as category_id, out_category as category, out_amount as amount, out_count as count from rpt.store_expenses(@c, @f, @t, @st)",
        ["customer-balances"] = "select out_customer_id as customer_id, out_customer as customer, out_balance as balance, out_last_entry as last_entry from rpt.customer_balances(@c)",
        ["price-bands"] = "select out_store_id as store_id, out_band_id as band_id, out_band_name as band, out_min_price as min_price, out_max_price as max_price, out_qty as qty, out_net as net, out_gross_profit as gross_profit, out_net_rank as net_rank, out_status as status from rpt.price_band_report(@c, @f, @t, @st)",
        ["article-store"] = "select out_product_id as product_id, out_store_id as store_id, out_qty as qty, out_net as net, out_gross_profit as gross_profit, out_store_rank as store_rank from rpt.article_store_report(@c, @f, @t, @st, @cat)",
        ["vendor-summary"] = "select * from purch.v_vendor_summary where payable_balance <> 0 or open_bill_remaining > 0",
        ["salary-outstanding"] = "select * from hr.v_salary_outstanding where (@st::bigint is null or store_id = @st) and outstanding_amount > 0",
        ["expense-store-month"] = "select * from exp.v_expense_store_month where (@st::bigint is null or store_id = @st) and period_month between date_trunc('month', @f::date) and @t",
        ["non-moving-stock"] = "select * from rpt.v_non_moving_stock where (@st::bigint is null or store_id = @st)",
        ["transfer-opportunities"] = "select * from rpt.v_transfer_opportunities",
        ["stock-exceptions"] = "select * from rpt.v_stock_exceptions_open",
    };

    public static bool Has(string key) => Sql.ContainsKey(key);

    public Task<List<Row>> RunReportAsync(string key, ReportArgs a, CancellationToken ct)
        => db.RowsAsync(Sql[key], new { c = a.CompanyId, f = a.From, t = a.To, st = a.StoreId, acc = a.AccountId, ve = a.VendorId, cu = a.CustomerId, cat = a.CategoryId, dt = a.DocType }, ct);

    private static object PlanArgs(long companyId, PlanInput p, long id = 0, long? rv = null) => new
    {
        c = companyId, id, rv, n = p.Name, at = p.AppliesTo, b = p.Basis, ct = p.CalcType, dr = p.DefaultRate, df = p.DefaultFixed, ms = p.MinStoreSales, st = p.StoreId, vf = p.ValidFrom, vt = p.ValidTo, a = p.IsActive,
    };
}
