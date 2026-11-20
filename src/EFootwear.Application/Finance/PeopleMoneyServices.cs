using EFootwear.Application.Abstractions;
using EFootwear.Application.Admin;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;

namespace EFootwear.Application.Finance;

public sealed record ExpenseCategoryBody(string? Code, string? Name, long GlAccountId, bool IsRecurring);
public sealed record StoreShare(long StoreId, long Amount);
public sealed record ExpenseBody(long? StoreId, long CategoryId, DateOnly? Date, long Amount, long? PayeeId, string? BillRef, string? Description, DateOnly? DueDate, string? Method,
                                 List<StoreShare>? Allocations, bool Submit = false, long? RowVer = null);
public sealed record PayBody(long Amount, string? Mode, DateOnly? Date, string? RefNo);
public sealed record RejectReason(string? Reason);

/// <summary>
/// Expenses: recorded as a draft, submitted, then approved by someone else (the database refuses self-approval) which books it against the
/// expense category's account and "expense payable". Head-office expenses may be split between stores. Payment is a separate voucher.
/// </summary>
public sealed class ExpenseService(IUnitOfWorkFactory uows, AccountingService accounting)
{
    public async Task<List<Row>> CategoriesAsync(UserContext u, CancellationToken ct)
    {
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Finance.ExpenseCategoriesAsync(ct);
    }

    public async Task<long> CreateCategoryAsync(UserContext u, ExpenseCategoryBody b, CancellationToken ct)
    {
        u.Require(P.MasterEdit);
        new Check().NotBlank(b.Code, "code").NotBlank(b.Name, "name").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        var a = await uow.Finance.AccountAsync(b.GlAccountId, ct) ?? throw new ValidationException("glAccountId", "Account not found.");
        if (a.Get<string>("mainType") != "EXPENSE") throw new ValidationException("glAccountId", "Choose an expense account.");
        var id = await uow.Finance.InsertExpenseCategoryAsync(u.CompanyId, b.Code!.Trim().ToUpperInvariant(), b.Name!.Trim(), b.GlAccountId, b.IsRecurring, ct);
        await uow.CommitAsync(ct);
        return id;
    }

    public async Task<Row> CreateAsync(UserContext u, ExpenseBody b, CancellationToken ct)
    {
        u.Require(P.ExpenseRecord);
        var store = b.StoreId ?? u.StoreId; u.RequireStore(store);
        Validate(b, store);
        await using var uow = await uows.BeginAsync(u, ct);
        var id = await uow.Finance.InsertExpenseAsync(Map(u, b, store), ct);
        await uow.Finance.ReplaceExpenseAllocationsAsync(u.CompanyId, id, (b.Allocations ?? []).Select(a => (a.StoreId, a.Amount)).ToList(), ct);
        if (b.Submit) await uow.Finance.SubmitExpenseAsync(u.CompanyId, id, u.UserId, ct);
        await uow.CommitAsync(ct);
        await using var read = await uows.BeginAsync(u, ct);
        return await DetailAsync(read, id, ct);
    }

    public async Task UpdateAsync(UserContext u, long id, ExpenseBody b, CancellationToken ct)
    {
        u.Require(P.ExpenseRecord);
        await using var uow = await uows.BeginAsync(u, ct);
        var cur = await uow.Finance.ExpenseAsync(id, ct) ?? throw new NotFoundException("Expense", id);
        u.RequireStore(cur.Get<long>("storeId"));
        Validate(b, cur.Get<long>("storeId"));
        if (!await uow.Finance.UpdateExpenseAsync(id, Map(u, b, cur.Get<long>("storeId")), b.RowVer, ct))
            throw new ConflictException("Only a draft or rejected expense can be edited, and it must not have changed meanwhile.", "stale");
        await uow.Finance.ReplaceExpenseAllocationsAsync(u.CompanyId, id, (b.Allocations ?? []).Select(a => (a.StoreId, a.Amount)).ToList(), ct);
        if (b.Submit) await uow.Finance.SubmitExpenseAsync(u.CompanyId, id, u.UserId, ct);
        await uow.CommitAsync(ct);
    }

    public async Task SubmitAsync(UserContext u, long id, CancellationToken ct)
    {
        u.Require(P.ExpenseRecord);
        await using var uow = await uows.BeginAsync(u, ct);
        var e = await uow.Finance.ExpenseAsync(id, ct) ?? throw new NotFoundException("Expense", id);
        u.RequireStore(e.Get<long>("storeId"));
        await uow.Finance.SubmitExpenseAsync(u.CompanyId, id, u.UserId, ct);
        await uow.CommitAsync(ct);
    }

    public async Task ApproveAsync(UserContext u, long id, CancellationToken ct)
    {
        u.Require(P.ExpenseApprove);
        await using (var check = await uows.BeginAsync(u, ct))
        {
            var e = await check.Finance.ExpenseAsync(id, ct) ?? throw new NotFoundException("Expense", id);
            u.RequireStore(e.Get<long>("storeId"));
        }
        // The expense-payable line belongs to head office (store 0), hidden from store users by row-level security: run the database function
        // in an all-stores scope. It re-checks EXPENSE_APPROVE and that the approver is not the submitter.
        await using var sys = await uows.BeginSystemAsync(u.CompanyId, u.UserId, ct);
        await sys.Finance.ApproveExpenseAsync(u.CompanyId, id, u.UserId, ct);
        await sys.CommitAsync(ct);
    }

    public async Task RejectAsync(UserContext u, long id, RejectReason b, CancellationToken ct)
    {
        u.Require(P.ExpenseApprove);
        new Check().NotBlank(b.Reason, "reason", "Reason").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        var e = await uow.Finance.ExpenseAsync(id, ct) ?? throw new NotFoundException("Expense", id);
        u.RequireStore(e.Get<long>("storeId"));
        await uow.Finance.RejectExpenseAsync(u.CompanyId, id, u.UserId, b.Reason!.Trim(), ct);
        await uow.CommitAsync(ct);
    }

    public async Task DeleteDraftAsync(UserContext u, long id, CancellationToken ct)
    {
        u.Require(P.ExpenseRecord);
        await using var uow = await uows.BeginAsync(u, ct);
        var e = await uow.Finance.ExpenseAsync(id, ct) ?? throw new NotFoundException("Expense", id);
        u.RequireStore(e.Get<long>("storeId"));
        if (!await uow.Finance.DeleteDraftExpenseAsync(id, ct)) throw new BusinessRuleException("Only a draft or rejected expense can be removed.", "not_draft");
        await uow.CommitAsync(ct);
    }


    /// <summary>Pay (part of) an approved expense: debits "expense payable" (a head-office account), credits cash or bank, and settles the expense.</summary>
    public async Task<Row> PayAsync(UserContext u, long id, PayBody b, CancellationToken ct)
    {
        u.RequireAny(P.VoucherPost, P.ExpenseRecord);
        new Check().Require(b.Amount > 0, "amount", "Amount must be above zero.").ThrowIfAny();
        await using (var check = await uows.BeginAsync(u, ct))
        {
            var e = await check.Finance.ExpenseAsync(id, ct) ?? throw new NotFoundException("Expense", id);
            u.RequireStore(e.Get<long>("storeId"));
        }
        long voucherId;
        await using (var sys = await uows.BeginSystemAsync(u.CompanyId, u.UserId, ct))   // the payable line is head office's (store 0): see ApproveAsync
        {
            voucherId = await accounting.PostExpensePaymentAsync(u, sys, id, b, ct);
            await sys.CommitAsync(ct);
        }
        await using var read = await uows.BeginAsync(u, ct);
        return new Row { ["expense"] = await DetailAsync(read, id, ct), ["voucherId"] = voucherId };
    }

    public async Task<Row> GetAsync(UserContext u, long id, CancellationToken ct)
    {
        u.RequireAny(P.ExpenseRecord, P.ExpenseApprove, P.ReportView, P.VoucherPost);
        await using var uow = await uows.BeginAsync(u, ct);
        return await DetailAsync(uow, id, ct);
    }

    public async Task<Page<Row>> ListAsync(UserContext u, ExpenseFilter f, PageQuery q, CancellationToken ct)
    {
        u.RequireAny(P.ExpenseRecord, P.ExpenseApprove, P.ReportView, P.VoucherPost);
        if (f.StoreId is { } s) u.RequireStore(s);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Finance.ExpensesAsync(f, q, ct);
    }

    private static async Task<Row> DetailAsync(IUnitOfWork uow, long id, CancellationToken ct)
    {
        var e = await uow.Finance.ExpenseAsync(id, ct) ?? throw new NotFoundException("Expense", id);
        e["allocations"] = await uow.Finance.ExpenseAllocationsAsync(id, ct);
        return e;
    }

    private static void Validate(ExpenseBody b, long store)
    {
        var c = new Check().Require(b.CategoryId > 0, "categoryId", "Choose a category.").Require(b.Amount > 0, "amount", "Amount must be above zero.")
            .Require(b.Method is null or "CASH" or "BANK" or "CHEQUE" or "OTHER", "method", "Method must be CASH, BANK, CHEQUE or OTHER.")
            .Require(b.DueDate is null || b.Date is null || b.DueDate >= b.Date, "dueDate", "The due date cannot be before the expense date.");
        var al = b.Allocations ?? [];
        if (al.Count > 0)
        {
            c.Require(store == 0, "allocations", "Only a head-office expense can be split between stores.");
            c.Require(al.Sum(a => a.Amount) == b.Amount, "allocations", "The shares must add up to the expense.");
            c.Require(al.All(a => a.Amount > 0 && a.StoreId > 0), "allocations", "Each share needs a selling store and an amount above zero.");
        }
        c.ThrowIfAny();
    }

    private static ExpenseInsert Map(UserContext u, ExpenseBody b, long store)
        => new(u.CompanyId, store, b.CategoryId, b.Date ?? DateOnly.FromDateTime(DateTime.UtcNow), b.Amount, b.PayeeId, StoreService.Blank(b.BillRef), StoreService.Blank(b.Description), b.DueDate, b.Method);
}

public sealed record PlanBody(string? Name, string? AppliesTo, string? Basis, string? CalcType, decimal? DefaultRate, long? DefaultFixed, long MinStoreSales, long? StoreId, DateOnly ValidFrom,
                              DateOnly? ValidTo, bool IsActive = true, long[]? Members = null, long? RowVer = null);
public sealed record RuleBody2(string? Scope, long? ProductId, long? CategoryId, long? PriceBandId, string? CalcType, decimal? Rate, long? Fixed, long MinLineAmount, int Priority, DateOnly ValidFrom, DateOnly? ValidTo);
public sealed record ApproveCommissionsBody(long StoreId, DateOnly From, DateOnly To);
public sealed record BuildPayrollBody(long StoreId, DateOnly Month);
public sealed record AdjustmentBody(long EmployeeId, DateOnly Month, string? Type, long Amount, string? Reason);
public sealed record PaySalaryBody(long Amount, string? Method, DateOnly? PaidOn, string? Reference);

/// <summary>
/// Commission plans and rules, commission approval, and monthly payroll. Commission is accrued per invoice line by the database; here managers set the
/// plans, approve accrued commission, build a salary sheet (base + approved commission + incentives − deductions), approve it (books the expense and
/// the salary payable) and record salary payments.
/// </summary>
public sealed class HrService(IUnitOfWorkFactory uows)
{
    public async Task<List<Row>> PlansAsync(UserContext u, CancellationToken ct)
    {
        u.RequireAny(P.CommissionView, P.CommissionApprove, P.PayrollPrepare, P.PayrollApprove);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Finance.PlansAsync(ct);
    }

    public async Task<Row> PlanAsync(UserContext u, long id, CancellationToken ct)
    {
        u.RequireAny(P.CommissionView, P.CommissionApprove, P.PayrollPrepare, P.PayrollApprove);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Finance.PlanAsync(id, ct) ?? throw new NotFoundException("Commission plan", id);
    }

    public async Task<long> CreatePlanAsync(UserContext u, PlanBody b, CancellationToken ct)
    {
        u.Require(P.CommissionApprove);
        var input = ValidatePlan(u, b);
        await using var uow = await uows.BeginAsync(u, ct);
        var id = await uow.Finance.InsertPlanAsync(u.CompanyId, input, ct);
        await uow.Finance.ReplacePlanMembersAsync(u.CompanyId, id, b.Members ?? [], ct);
        await uow.CommitAsync(ct);
        return id;
    }

    public async Task UpdatePlanAsync(UserContext u, long id, PlanBody b, CancellationToken ct)
    {
        u.Require(P.CommissionApprove);
        var input = ValidatePlan(u, b);
        await using var uow = await uows.BeginAsync(u, ct);
        if (!await uow.Finance.UpdatePlanAsync(id, input, b.RowVer, ct)) throw new ConflictException("This plan was changed by someone else, or no longer exists.", "stale");
        await uow.Finance.ReplacePlanMembersAsync(u.CompanyId, id, b.Members ?? [], ct);
        await uow.CommitAsync(ct);
    }

    public async Task<long> AddRuleAsync(UserContext u, long planId, RuleBody2 b, CancellationToken ct)
    {
        u.Require(P.CommissionApprove);
        var c = new Check().Require(b.Scope is "PRODUCT" or "CATEGORY" or "PRICE_BAND", "scope", "Scope must be PRODUCT, CATEGORY or PRICE_BAND.")
            .Require(b.CalcType is "PERCENT" or "FIXED_PER_PIECE", "calcType", "Calculation must be PERCENT or FIXED_PER_PIECE.")
            .Require(b.CalcType != "PERCENT" || b.Rate is >= 0 and <= 100, "rate", "A percentage rate must be between 0 and 100.")
            .Require(b.CalcType != "FIXED_PER_PIECE" || b.Fixed is >= 0, "fixed", "A per-piece amount cannot be negative.")
            .Require(b.ValidTo is null || b.ValidTo >= b.ValidFrom, "validTo", "The end cannot be before the start.");
        c.ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        if (await uow.Finance.PlanAsync(planId, ct) is null) throw new NotFoundException("Commission plan", planId);
        var id = await uow.Finance.InsertCommissionRuleAsync(u.CompanyId, planId, new CommissionRuleInput(b.Scope!, b.ProductId, b.CategoryId, b.PriceBandId, b.CalcType!,
            b.CalcType == "PERCENT" ? b.Rate : null, b.CalcType == "FIXED_PER_PIECE" ? b.Fixed : null, b.MinLineAmount, b.Priority, b.ValidFrom, b.ValidTo), ct);
        await uow.CommitAsync(ct);
        return id;
    }

    public async Task EndRuleAsync(UserContext u, long ruleId, DateOnly validTo, CancellationToken ct)
    {
        u.Require(P.CommissionApprove);
        await using var uow = await uows.BeginAsync(u, ct);
        await uow.Finance.EndCommissionRuleAsync(ruleId, validTo, ct);
        await uow.CommitAsync(ct);
    }

    public async Task<Page<Row>> EntriesAsync(UserContext u, long? storeId, long? employeeId, string? status, DateOnly? from, DateOnly? to, PageQuery q, CancellationToken ct)
    {
        u.RequireAny(P.CommissionView, P.CommissionApprove);
        if (storeId is { } s) u.RequireStore(s);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Finance.CommissionEntriesAsync(storeId, employeeId, status, from, to, q, ct);
    }

    public async Task<List<Row>> SummaryAsync(UserContext u, long? storeId, DateOnly? month, CancellationToken ct)
    {
        u.RequireAny(P.CommissionView, P.CommissionApprove);
        if (storeId is { } s) u.RequireStore(s);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Finance.CommissionSummaryAsync(storeId, month, ct);
    }

    public async Task<int> ApproveCommissionsAsync(UserContext u, ApproveCommissionsBody b, CancellationToken ct)
    {
        u.Require(P.CommissionApprove); u.RequireStore(b.StoreId);
        new Check().Require(b.To >= b.From, "to", "The end cannot be before the start.").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        var n = await uow.Finance.ApproveCommissionsAsync(u.CompanyId, b.StoreId, b.From, b.To, u.UserId, ct);
        await uow.CommitAsync(ct);
        return n;
    }

    // ------------------------------------------------------------------------------------------ payroll

    public async Task<Row> BuildPayrollAsync(UserContext u, BuildPayrollBody b, CancellationToken ct)
    {
        u.Require(P.PayrollPrepare); u.RequireStore(b.StoreId);
        await using var uow = await uows.BeginAsync(u, ct);
        var id = await uow.Finance.BuildPayrollAsync(u.CompanyId, b.StoreId, b.Month, u.UserId, ct);
        await uow.CommitAsync(ct);
        await using var read = await uows.BeginAsync(u, ct);
        return await RunDetailAsync(read, id, ct);
    }

    public async Task<long> AddAdjustmentAsync(UserContext u, AdjustmentBody b, CancellationToken ct)
    {
        u.Require(P.PayrollPrepare);
        new Check().Require(b.Type is "INCENTIVE" or "DEDUCTION" or "ADVANCE", "type", "Type must be INCENTIVE, DEDUCTION or ADVANCE.").Require(b.Amount > 0, "amount", "Amount must be above zero.").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        var id = await uow.Finance.InsertAdjustmentAsync(u.CompanyId, b.EmployeeId, b.Month, b.Type!, b.Amount, b.Reason, ct);
        await uow.CommitAsync(ct);
        return id;
    }

    public async Task<Row> ApprovePayrollAsync(UserContext u, long runId, CancellationToken ct)
    {
        u.Require(P.PayrollApprove);
        await using (var check = await uows.BeginAsync(u, ct))
        {
            var r = await check.Finance.PayrollRunAsync(runId, ct) ?? throw new NotFoundException("Payroll", runId);
            u.RequireStore(r.Get<long>("storeId"));
        }
        await using (var sys = await uows.BeginSystemAsync(u.CompanyId, u.UserId, ct))   // the salary-payable line is head office's (store 0)
        {
            await sys.Finance.ApprovePayrollAsync(u.CompanyId, runId, u.UserId, ct);
            await sys.CommitAsync(ct);
        }
        await using var read = await uows.BeginAsync(u, ct);
        return await RunDetailAsync(read, runId, ct);
    }

    public async Task CancelDraftPayrollAsync(UserContext u, long runId, CancellationToken ct)
    {
        u.Require(P.PayrollPrepare);
        await using var uow = await uows.BeginAsync(u, ct);
        var r = await uow.Finance.PayrollRunAsync(runId, ct) ?? throw new NotFoundException("Payroll", runId);
        u.RequireStore(r.Get<long>("storeId"));
        if (!await uow.Finance.CancelDraftRunAsync(u.CompanyId, runId, ct)) throw new BusinessRuleException("Only a draft salary sheet can be cancelled.", "not_draft");
        await uow.CommitAsync(ct);
    }

    public async Task<Row> PaySalaryAsync(UserContext u, long lineId, PaySalaryBody b, CancellationToken ct)
    {
        u.Require(P.PayrollApprove);
        new Check().Require(b.Amount > 0, "amount", "Amount must be above zero.").Require((b.Method ?? "").ToUpperInvariant() is "CASH" or "BANK" or "OTHER", "method", "Method must be CASH, BANK or OTHER.").ThrowIfAny();
        await using (var check = await uows.BeginAsync(u, ct))
        {
            var l = await check.Finance.PayrollLineAsync(lineId, ct) ?? throw new NotFoundException("Salary line", lineId);
            u.RequireStore(l.Get<long>("storeId"));
        }
        await using (var sys = await uows.BeginSystemAsync(u.CompanyId, u.UserId, ct))   // the salary-payable line is head office's (store 0)
        {
            await sys.Finance.PaySalaryAsync(u.CompanyId, lineId, b.Amount, b.Method!.ToUpperInvariant(), b.PaidOn ?? DateOnly.FromDateTime(DateTime.UtcNow), u.UserId, StoreService.Blank(b.Reference), ct);
            await sys.CommitAsync(ct);
        }
        await using var read = await uows.BeginAsync(u, ct);
        var line = (await read.Finance.PayrollLineAsync(lineId, ct))!;
        line["payments"] = await read.Finance.SalaryPaymentsAsync(lineId, ct);
        return line;
    }

    public async Task<List<Row>> RunsAsync(UserContext u, long? storeId, CancellationToken ct)
    {
        u.RequireAny(P.PayrollPrepare, P.PayrollApprove);
        if (storeId is { } s) u.RequireStore(s);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Finance.PayrollRunsAsync(storeId, ct);
    }

    public async Task<Row> RunAsync(UserContext u, long id, CancellationToken ct)
    {
        u.RequireAny(P.PayrollPrepare, P.PayrollApprove);
        await using var uow = await uows.BeginAsync(u, ct);
        return await RunDetailAsync(uow, id, ct);
    }

    private static async Task<Row> RunDetailAsync(IUnitOfWork uow, long id, CancellationToken ct)
    {
        var r = await uow.Finance.PayrollRunAsync(id, ct) ?? throw new NotFoundException("Payroll", id);
        r["lines"] = await uow.Finance.PayrollLinesAsync(id, ct);
        return r;
    }

    private static PlanInput ValidatePlan(UserContext u, PlanBody b)
    {
        new Check().NotBlank(b.Name, "name").Require(b.AppliesTo is "SALESMAN" or "MANAGER", "appliesTo", "Applies to SALESMAN or MANAGER.")
            .Require(b.Basis is "NET_SALE" or "GROSS_MARGIN", "basis", "Basis must be NET_SALE or GROSS_MARGIN.")
            .Require(b.CalcType is "PERCENT" or "FIXED_PER_PIECE", "calcType", "Calculation must be PERCENT or FIXED_PER_PIECE.")
            .Require(b.CalcType != "PERCENT" || b.DefaultRate is >= 0 and <= 100, "defaultRate", "A percentage rate must be between 0 and 100.")
            .Require(b.CalcType != "FIXED_PER_PIECE" || b.DefaultFixed is >= 0, "defaultFixed", "A per-piece amount cannot be negative.")
            .Require(b.MinStoreSales >= 0, "minStoreSales", "Minimum store sales cannot be negative.")
            .Require(b.ValidTo is null || b.ValidTo >= b.ValidFrom, "validTo", "The end cannot be before the start.").ThrowIfAny();
        if (b.StoreId is { } s) u.RequireStore(s);
        return new PlanInput(b.Name!.Trim(), b.AppliesTo!, b.Basis!, b.CalcType!, b.CalcType == "PERCENT" ? b.DefaultRate : null, b.CalcType == "FIXED_PER_PIECE" ? b.DefaultFixed : null,
            b.MinStoreSales, b.StoreId, b.ValidFrom, b.ValidTo, b.IsActive);
    }
}

public sealed record PromiseBody(long? StoreId, long CustomerId, DateOnly DueDate, long Amount, string? Note);
public sealed record PromisePayment(long Amount);
public sealed record CashCountBody(long? StoreId, DateOnly? Date, long CountedCash, Dictionary<int, int>? Denominations, string? Note);

/// <summary>Customer payment promises and the end-of-day cash count (expected cash comes from the books, counted cash from the denominations).</summary>
public sealed class CashService(IUnitOfWorkFactory uows)
{
    private static readonly int[] Notes = [5000, 1000, 500, 100, 50, 20, 10, 5, 2, 1];

    public async Task<Page<Row>> PromisesAsync(UserContext u, long? storeId, long? customerId, string? status, PageQuery q, CancellationToken ct)
    {
        if (storeId is { } s) u.RequireStore(s);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Finance.PromisesAsync(storeId, customerId, status, q, ct);
    }

    public async Task<long> CreatePromiseAsync(UserContext u, PromiseBody b, CancellationToken ct)
    {
        u.RequireAny(P.SaleCreate, P.VoucherPost);
        var store = b.StoreId ?? u.StoreId; u.RequireStore(store);
        new Check().Require(b.Amount > 0, "amount", "Amount must be above zero.").Require(b.DueDate >= DateOnly.FromDateTime(DateTime.UtcNow), "dueDate", "The due date cannot be in the past.").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        var id = await uow.Finance.InsertPromiseAsync(u.CompanyId, store, b.CustomerId, b.DueDate, b.Amount, b.Note, ct);
        await uow.CommitAsync(ct);
        return id;
    }

    public async Task<Row> RecordPromisePaymentAsync(UserContext u, long id, PromisePayment b, CancellationToken ct)
    {
        u.RequireAny(P.SaleCreate, P.VoucherPost);
        new Check().Require(b.Amount > 0, "amount", "Amount must be above zero.").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        var p = await uow.Finance.PromiseAsync(id, ct) ?? throw new NotFoundException("Promise", id);
        u.RequireStore(p.Get<long>("storeId"));
        if (p.Get<string>("status") is "PAID" or "CANCELLED") throw new BusinessRuleException("This promise is already settled or cancelled.", "closed");
        var paid = p.Get<long>("paidAmount") + b.Amount;
        if (paid > p.Get<long>("promisedAmount")) throw new BusinessRuleException("That is more than was promised.", "over_payment");
        await uow.Finance.UpdatePromiseAsync(id, paid, paid == p.Get<long>("promisedAmount") ? "PAID" : "PARTIAL", ct);
        await uow.CommitAsync(ct);
        await using var read = await uows.BeginAsync(u, ct);
        return (await read.Finance.PromiseAsync(id, ct))!;
    }

    public async Task CancelPromiseAsync(UserContext u, long id, CancellationToken ct)
    {
        u.RequireAny(P.SaleCreate, P.VoucherPost);
        await using var uow = await uows.BeginAsync(u, ct);
        var p = await uow.Finance.PromiseAsync(id, ct) ?? throw new NotFoundException("Promise", id);
        u.RequireStore(p.Get<long>("storeId"));
        await uow.Finance.UpdatePromiseAsync(id, p.Get<long>("paidAmount"), "CANCELLED", ct);
        await uow.CommitAsync(ct);
    }

    public async Task<Row> ExpectedAsync(UserContext u, long? storeId, DateOnly? date, CancellationToken ct)
    {
        u.RequireAny(P.CashCount, P.ReportView, P.VoucherPost);
        var store = storeId ?? u.StoreId; u.RequireStore(store);
        var d = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        await using var uow = await uows.BeginAsync(u, ct);
        return new Row { ["storeId"] = store, ["date"] = d, ["expectedCash"] = await uow.Finance.ExpectedCashAsync(store, d, ct), ["openingCash"] = await uow.Finance.LastCountedCashAsync(store, d, ct) ?? 0L };
    }

    public async Task<Row> CountAsync(UserContext u, CashCountBody b, CancellationToken ct)
    {
        u.Require(P.CashCount);
        var store = b.StoreId ?? u.StoreId; u.RequireStore(store);
        var den = b.Denominations ?? [];
        new Check().Require(b.CountedCash >= 0, "countedCash", "Counted cash cannot be negative.").Require(den.Keys.All(k => Notes.Contains(k)), "denominations", "Unknown note or coin value.")
            .Require(den.Values.All(v => v >= 0), "denominations", "A count cannot be negative.")
            .Require(den.Count == 0 || den.Sum(kv => (long)kv.Key * kv.Value) == b.CountedCash, "countedCash", "The counted cash must equal the sum of the denominations.").ThrowIfAny();
        var date = b.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        await using var uow = await uows.BeginAsync(u, ct);
        var expected = await uow.Finance.ExpectedCashAsync(store, date, ct);
        var opening = await uow.Finance.LastCountedCashAsync(store, date, ct) ?? 0;
        var id = await uow.Finance.InsertCashCountAsync(u.CompanyId, store, date, opening, expected, b.CountedCash, b.Note, u.UserId, ct);
        foreach (var (k, v) in den.Where(x => x.Value > 0)) await uow.Finance.InsertDenominationAsync(u.CompanyId, id, k, v, ct);
        await uow.CommitAsync(ct);
        await using var read = await uows.BeginAsync(u, ct);
        return (await read.Finance.CashCountAsync(id, ct))!;
    }

    public async Task<List<Row>> CountsAsync(UserContext u, long? storeId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        u.RequireAny(P.CashCount, P.ReportView, P.VoucherPost);
        if (storeId is { } s) u.RequireStore(s);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Finance.CashCountsAsync(storeId, from, to, ct);
    }
}

public sealed record ReportMeta(string Key, string Title, string Group, string Permission, string[] Filters);

/// <summary>The report catalogue. The database functions behind the reports check REPORT_VIEW / REPORT_ACCOUNTS again; row-level security limits stores.</summary>
public sealed class ReportService(IUnitOfWorkFactory uows)
{
    public static readonly IReadOnlyList<ReportMeta> Catalog =
    [
        new("account-ledger", "Account ledger", "Accounts", P.ReportAccounts, ["account", "dates", "store"]),
        new("trial-balance", "Trial balance", "Accounts", P.ReportAccounts, ["dates", "store"]),
        new("account-balance", "Account balances", "Accounts", P.ReportAccounts, ["asOf", "store"]),
        new("general-journal", "General journal", "Accounts", P.ReportAccounts, ["dates", "store"]),
        new("sale-register", "Sale register", "Sales", P.ReportView, ["dates", "store", "docType", "customer"]),
        new("sale-by-category", "Sales by category", "Sales", P.ReportView, ["dates", "store"]),
        new("profit-summary", "Profit summary", "Sales", P.ReportView, ["dates", "store"]),
        new("price-bands", "Sales by price band", "Sales", P.ReportView, ["dates", "store"]),
        new("article-store", "Article by store", "Sales", P.ReportView, ["dates", "store", "category"]),
        new("customer-balances", "Customer balances", "Parties", P.ReportView, []),
        new("purchase-register", "Purchase register", "Purchases", P.ReportView, ["dates", "store", "vendor"]),
        new("vendor-aging", "Vendor aging", "Purchases", P.ReportView, ["asOf"]),
        new("vendor-summary", "Vendor payables", "Purchases", P.ReportView, []),
        new("stock-value", "Stock value", "Stock", P.ReportView, ["store", "category"]),
        new("stock-adjustments", "Stock adjustments", "Stock", P.ReportView, ["dates", "store"]),
        new("non-moving-stock", "Non-moving stock", "Stock", P.ReportView, ["store"]),
        new("transfer-opportunities", "Transfer opportunities", "Stock", P.ReportView, []),
        new("stock-exceptions", "Open stock shortfalls", "Stock", P.ReportView, []),
        new("commission-accrual", "Commission accrual", "People", P.ReportView, ["dates", "store"]),
        new("salary-outstanding", "Salary outstanding", "People", P.ReportView, ["store"]),
        new("store-expenses", "Expenses by store", "Expenses", P.ReportView, ["dates", "store"]),
        new("expense-store-month", "Expenses by month", "Expenses", P.ReportView, ["dates", "store"]),
    ];

    /// <summary>The reports this user may run.</summary>
    public IReadOnlyList<ReportMeta> For(UserContext u) => Catalog.Where(r => u.Has(r.Permission)).ToList();

    public async Task<Row> RunAsync(UserContext u, string key, DateOnly? from, DateOnly? to, long? storeId, long? accountId, long? vendorId, long? customerId, long? categoryId, string? docType, CancellationToken ct)
    {
        var meta = Catalog.FirstOrDefault(r => r.Key == key) ?? throw new NotFoundException("Report", key);
        u.Require(meta.Permission);
        if (storeId is { } s) u.RequireStore(s);
        if (key == "account-ledger" && accountId is null) throw new ValidationException("accountId", "Choose an account.");
        var t = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var f = from ?? new DateOnly(t.Year, t.Month, 1);
        if (t < f) throw new ValidationException("to", "The end cannot be before the start.");
        await using var uow = await uows.BeginAsync(u, ct);
        var rows = await uow.Finance.RunReportAsync(key, new ReportArgs(u.CompanyId, f, t, storeId, accountId, vendorId, customerId, categoryId, docType), ct);
        return new Row { ["key"] = key, ["title"] = meta.Title, ["from"] = f, ["to"] = t, ["rows"] = rows };
    }
}
