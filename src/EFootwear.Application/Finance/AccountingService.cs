using EFootwear.Application.Abstractions;
using EFootwear.Application.Admin;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;

namespace EFootwear.Application.Finance;

public sealed record AccountBody(string? Code, string? MainType, string? GroupCode, string? Name, long? ParentId, long? StoreId, bool IsPostable = true, bool IsActive = true, long? RowVer = null);
public sealed record AllocationBody(long? PurchaseInvoiceId, long? ExpenseId, long Amount);

public sealed record VoucherBody(string? Type, long? StoreId, DateOnly? Date, long? PartyId, long Amount, string? Mode, string? RefNo, string? Description,
                                 long? DebitAccountId, long? CreditAccountId, long? ExpenseAccountId, List<AllocationBody>? Allocations);

public sealed record OpeningItem(long? AccountId, long? PartyId, long Debit, long Credit);
public sealed record OpeningBody(long? StoreId, DateOnly? Date, List<OpeningItem>? Items, string? Note);

public sealed record SchedulePlanBody(long? StoreId, long VendorId, long? PurchaseInvoiceId, DateOnly DueDate, long Amount, string? Note);

/// <summary>
/// Chart of accounts, vouchers (payment, receipt, expense, journal), party ledgers, vendor payables and opening balances. Every voucher writes one
/// balanced journal entry; voiding writes the mirror entry (nothing is edited or deleted from the ledger).
/// </summary>
public sealed class AccountingService(IUnitOfWorkFactory uows)
{
    private static readonly string[] MainTypes = ["ASSET", "LIABILITY", "CAPITAL", "INCOME", "STOCK", "EXPENSE"];
    private static readonly string[] Modes = ["CASH", "BANK", "CHEQUE", "ONLINE", "OTHER"];

    // ------------------------------------------------------------------------------------------ accounts

    public async Task<List<Row>> AccountsAsync(UserContext u, bool includeInactive, CancellationToken ct)
    {
        u.RequireAny(P.VoucherPost, P.OpeningBalance, P.ReportView, P.ReportAccounts, P.ExpenseRecord, P.PurchasePay);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Finance.AccountsAsync(includeInactive, ct);
    }

    public async Task<long> CreateAccountAsync(UserContext u, AccountBody b, CancellationToken ct)
    {
        u.Require(P.MasterEdit);
        var storeId = b.StoreId ?? 0;
        new Check().NotBlank(b.Code, "code").NotBlank(b.Name, "name").NotBlank(b.GroupCode, "groupCode", "Group").Require(MainTypes.Contains(b.MainType ?? ""), "mainType", "Choose a valid account type.").ThrowIfAny();
        u.RequireStore(storeId);
        await using var uow = await uows.BeginAsync(u, ct);
        var id = await uow.Finance.InsertAccountAsync(u.CompanyId, b.Code!.Trim().ToUpperInvariant(), b.MainType!, b.GroupCode!.Trim(), b.Name!.Trim(), b.ParentId, storeId, b.IsPostable, ct);
        await uow.CommitAsync(ct);
        return id;
    }

    public async Task UpdateAccountAsync(UserContext u, long id, AccountBody b, CancellationToken ct)
    {
        u.Require(P.MasterEdit);
        new Check().NotBlank(b.Name, "name").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        var a = await uow.Finance.AccountAsync(id, ct) ?? throw new NotFoundException("Account", id);
        if (a.Get<string?>("systemRole") is not null && !b.IsActive) throw new BusinessRuleException("A system account the application posts to cannot be deactivated.", "system_account");
        if (!await uow.Finance.UpdateAccountAsync(id, b.Name!.Trim(), b.IsActive, b.RowVer, ct)) throw new ConflictException("This account was changed by someone else. Reload and try again.", "stale");
        await uow.CommitAsync(ct);
    }

    // ------------------------------------------------------------------------------------------ party ledger

    public async Task<Row> PartyLedgerAsync(UserContext u, long partyId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        u.RequireAny(P.VoucherPost, P.ReportView, P.ReportAccounts, P.SaleCreate, P.PurchaseCreate, P.PurchasePay);
        var f = from ?? new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1); var t = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        await using var uow = await uows.BeginAsync(u, ct);
        var party = await uow.Parties.GetAsync(partyId, ct) ?? throw new NotFoundException("Party", partyId);
        var lines = await uow.Finance.PartyLedgerAsync(partyId, f, t, ct);
        var opening = await uow.Finance.PartyOpeningAsync(partyId, f, ct);
        return new Row
        {
            ["party"] = party, ["from"] = f, ["to"] = t, ["opening"] = opening, ["lines"] = lines,
            ["totalDebit"] = lines.Sum(l => l.Get<long>("debit")), ["totalCredit"] = lines.Sum(l => l.Get<long>("credit")),
            ["closing"] = opening + lines.Sum(l => l.Get<long>("debit") - l.Get<long>("credit")),
        };
    }

    // ------------------------------------------------------------------------------------------ vouchers

    public async Task<Row> CreateVoucherAsync(UserContext u, VoucherBody b, CancellationToken ct)
    {
        u.RequireAny(P.VoucherPost, P.PurchasePay, P.ExpenseRecord);
        await using var uow = await uows.BeginAsync(u, ct);
        var id = await PostVoucherAsync(u, uow, b, ct);
        await uow.CommitAsync(ct);
        await using var read = await uows.BeginAsync(u, ct);
        return await VoucherDetailAsync(read, id, ct);
    }

    /// <summary>Builds and stores one voucher with its journal entry and settlement rows inside the caller's transaction.</summary>
    internal async Task<long> PostVoucherAsync(UserContext u, IUnitOfWork uow, VoucherBody b, CancellationToken ct)
    {
        var type = (b.Type ?? "").ToUpperInvariant();
        var c = new Check().Require(type is "PAYMENT" or "RECEIPT" or "EXPENSE" or "JOURNAL", "type", "Type must be PAYMENT, RECEIPT, EXPENSE or JOURNAL.")
            .Require(b.Amount > 0, "amount", "Amount must be above zero.").MaxLength(b.RefNo, 60, "refNo");
        if (type != "JOURNAL") c.Require(Modes.Contains((b.Mode ?? "").ToUpperInvariant()), "mode", "Choose how it was paid: CASH, BANK, CHEQUE, ONLINE or OTHER.");
        c.ThrowIfAny();
        if (type is "PAYMENT" && b.Allocations is { Count: > 0 }) u.RequireAny(P.VoucherPost, P.PurchasePay, P.ExpenseRecord);
        else if (type is "PAYMENT" or "RECEIPT" or "JOURNAL") u.RequireAny(P.VoucherPost, P.PurchasePay);
        if (type == "EXPENSE") u.RequireAny(P.VoucherPost, P.ExpenseRecord);

        var store = b.StoreId ?? u.StoreId;
        u.RequireStore(store);
        var date = b.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var mode = (b.Mode ?? "CASH").ToUpperInvariant();
        var money = await uow.Finance.RoleAccountAsync(0, mode == "CASH" ? "CASH" : "BANK", ct);
        long debit, credit; long? partyOnDebit = null, partyOnCredit = null;

        switch (type)
        {
            case "PAYMENT":
            case "RECEIPT":
            {
                var party = await RequirePartyAsync(uow, b.PartyId, type == "RECEIPT" ? "customerId" : "partyId", ct);
                var control = await uow.Finance.RoleAccountAsync(0, "PARTY_CONTROL", ct);
                if (type == "PAYMENT") { debit = control; credit = money; partyOnDebit = party; }
                else { debit = money; credit = control; partyOnCredit = party; }
                break;
            }
            case "EXPENSE":
            {
                var acc = b.ExpenseAccountId ?? b.DebitAccountId ?? throw new ValidationException("expenseAccountId", "Choose the expense account.");
                var a = await uow.Finance.AccountAsync(acc, ct) ?? throw new ValidationException("expenseAccountId", "Account not found.");
                if (a.Get<string>("mainType") != "EXPENSE" || !a.Get<bool>("isPostable")) throw new ValidationException("expenseAccountId", "Choose an expense account that accepts postings.");
                debit = acc; credit = money;
                break;
            }
            default:
            {
                if (b.DebitAccountId is null || b.CreditAccountId is null) throw new ValidationException("debitAccountId", "A journal voucher needs a debit and a credit account.");
                if (b.DebitAccountId == b.CreditAccountId) throw new ValidationException("creditAccountId", "Debit and credit accounts must differ.");
                var d = await uow.Finance.AccountAsync(b.DebitAccountId.Value, ct) ?? throw new ValidationException("debitAccountId", "Account not found.");
                var k = await uow.Finance.AccountAsync(b.CreditAccountId.Value, ct) ?? throw new ValidationException("creditAccountId", "Account not found.");
                foreach (var a in new[] { d, k }) if (!a.Get<bool>("isPostable") || !a.Get<bool>("isActive")) throw new BusinessRuleException($"Account {a.Get<string>("code")} does not accept postings.", "not_postable");
                if (d.Get<bool>("isPartyControl") || k.Get<bool>("isPartyControl"))
                {
                    var party = await RequirePartyAsync(uow, b.PartyId, "partyId", ct);
                    if (d.Get<bool>("isPartyControl")) partyOnDebit = party; else partyOnCredit = party;
                }
                debit = b.DebitAccountId.Value; credit = b.CreditAccountId.Value;
                break;
            }
        }

        var no = await uow.Finance.NextNumberAsync(u.CompanyId, store, "VOUCHER-" + type, ct);
        var entry = await uow.Finance.InsertJournalAsync(u.CompanyId, date, store, "VOUCHER", null, $"{type.ToLowerInvariant()} voucher {no}", u.UserId, ct);
        await uow.Finance.AddLineAsync(u.CompanyId, entry, date, 1, store, debit, partyOnDebit, b.Amount, 0, StoreService.Blank(b.Description), ct);
        await uow.Finance.AddLineAsync(u.CompanyId, entry, date, 2, store, credit, partyOnCredit, 0, b.Amount, StoreService.Blank(b.Description), ct);
        var id = await uow.Finance.InsertVoucherAsync(new VoucherInsert(u.CompanyId, store, type, no, date, debit, credit, b.Amount, partyOnDebit ?? partyOnCredit, type == "JOURNAL" ? null : mode,
            StoreService.Blank(b.RefNo), StoreService.Blank(b.Description), entry), ct);
        await AllocateAsync(u, uow, id, store, type, b, partyOnDebit ?? partyOnCredit, ct);
        return id;
    }

    /// <summary>Pays an approved expense: Dr expense payable (head office) / Cr cash or bank, one voucher, settles the expense. Runs in the caller's (all-stores) transaction.</summary>
    internal async Task<long> PostExpensePaymentAsync(UserContext u, IUnitOfWork uow, long expenseId, PayBody b, CancellationToken ct)
    {
        var e = await uow.Finance.ExpenseForPaymentAsync(expenseId, ct) ?? throw new NotFoundException("Expense", expenseId);
        if (e.Get<string>("status") != "POSTED") throw new BusinessRuleException("Only an approved expense can be paid.", "not_posted");
        var left = e.Get<long>("amount") - e.Get<long>("paidAmount");
        if (b.Amount > left) throw new BusinessRuleException($"Only {left} is left to pay on this expense.", "over_payment");
        var mode = (b.Mode ?? "CASH").ToUpperInvariant();
        if (!Modes.Contains(mode)) throw new ValidationException("mode", "Choose CASH, BANK, CHEQUE, ONLINE or OTHER.");
        var store = e.Get<long>("storeId");
        var date = b.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var payable = await uow.Finance.RoleAccountAsync(0, "EXPENSE_PAYABLE", ct);
        var money = await uow.Finance.RoleAccountAsync(0, mode == "CASH" ? "CASH" : "BANK", ct);
        var no = await uow.Finance.NextNumberAsync(u.CompanyId, store, "VOUCHER-PAYMENT", ct);
        var desc = $"Payment of expense {expenseId}";
        var entry = await uow.Finance.InsertJournalAsync(u.CompanyId, date, store, "VOUCHER", null, desc, u.UserId, ct);
        await uow.Finance.AddLineAsync(u.CompanyId, entry, date, 1, 0, payable, null, b.Amount, 0, desc, ct);
        await uow.Finance.AddLineAsync(u.CompanyId, entry, date, 2, store, money, null, 0, b.Amount, desc, ct);
        var id = await uow.Finance.InsertVoucherAsync(new VoucherInsert(u.CompanyId, store, "PAYMENT", no, date, payable, money, b.Amount, null, mode, StoreService.Blank(b.RefNo), desc, entry), ct);
        await uow.Finance.InsertAllocationAsync(u.CompanyId, id, store, null, null, expenseId, b.Amount, ct);
        var paid = e.Get<long>("paidAmount") + b.Amount;
        await uow.Finance.SetExpensePaidAsync(expenseId, paid, paid >= e.Get<long>("amount") ? "PAID" : "PARTIAL", ct);
        return id;
    }

    private static async Task<long> RequirePartyAsync(IUnitOfWork uow, long? partyId, string field, CancellationToken ct)
    {
        if (partyId is null) throw new ValidationException(field, "Choose the party.");
        var p = await uow.Sales.PartyBriefAsync(partyId.Value, ct) ?? throw new ValidationException(field, "Party not found.");
        if (p.Get<string>("status") != "ACTIVE") throw new ValidationException(field, "This party is inactive.");
        return partyId.Value;
    }

    private static async Task AllocateAsync(UserContext u, IUnitOfWork uow, long voucherId, long store, string type, VoucherBody b, long? party, CancellationToken ct)
    {
        var allocs = b.Allocations ?? [];
        if (allocs.Count == 0) return;
        if (type is not ("PAYMENT" or "EXPENSE")) throw new ValidationException("allocations", "Only a payment can settle bills.");
        if (allocs.Sum(a => a.Amount) > b.Amount) throw new ValidationException("allocations", "The settled amounts are more than the voucher.");
        foreach (var a in allocs)
        {
            if (a.Amount <= 0) throw new ValidationException("allocations", "Each settled amount must be above zero.");
            if ((a.PurchaseInvoiceId is null) == (a.ExpenseId is null)) throw new ValidationException("allocations", "Each line settles one purchase bill or one expense.");
            if (a.PurchaseInvoiceId is { } pi)
            {
                var bal = await uow.Finance.PurchaseBalanceAsync(pi, ct) ?? throw new BusinessRuleException($"Purchase {pi} is not an open bill.", "not_open");
                if (party is not null && bal.Get<long>("vendorPartyId") != party) throw new BusinessRuleException($"Purchase {pi} belongs to another vendor.", "wrong_vendor");
                if (a.Amount > bal.Get<long>("remainingAmount")) throw new BusinessRuleException($"Purchase {pi} has only {bal.Get<long>("remainingAmount")} left to pay.", "over_payment");
                await uow.Finance.InsertAllocationAsync(u.CompanyId, voucherId, store, pi, null, null, a.Amount, ct);
            }
            else
            {
                var e = await uow.Finance.ExpenseForPaymentAsync(a.ExpenseId!.Value, ct) ?? throw new NotFoundException("Expense", a.ExpenseId);
                if (e.Get<string>("status") != "POSTED") throw new BusinessRuleException("Only an approved expense can be paid.", "not_posted");
                var left = e.Get<long>("amount") - e.Get<long>("paidAmount");
                if (a.Amount > left) throw new BusinessRuleException($"Expense {a.ExpenseId} has only {left} left to pay.", "over_payment");
                await uow.Finance.InsertAllocationAsync(u.CompanyId, voucherId, store, null, null, a.ExpenseId, a.Amount, ct);
                var paid = e.Get<long>("paidAmount") + a.Amount;
                await uow.Finance.SetExpensePaidAsync(a.ExpenseId.Value, paid, paid >= e.Get<long>("amount") ? "PAID" : "PARTIAL", ct);
            }
        }
    }

    public async Task VoidVoucherAsync(UserContext u, long id, VoidReason b, CancellationToken ct)
    {
        u.Require(P.VoucherDelete);
        new Check().NotBlank(b.Reason, "reason", "Reason").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        var v = await uow.Finance.VoucherAsync(id, ct) ?? throw new NotFoundException("Voucher", id);
        u.RequireStore(v.Get<long>("storeId"));
        if (v.Get<string>("status") != "POSTED") throw new BusinessRuleException("This voucher is already void.", "already_void");
        foreach (var a in await uow.Finance.VoucherAllocationsAsync(id, ct))
            if (a.Get<long?>("expenseId") is { } ex)
            {
                var e = (await uow.Finance.ExpenseForPaymentAsync(ex, ct))!;
                var paid = Math.Max(0, e.Get<long>("paidAmount") - a.Get<long>("amount"));
                await uow.Finance.SetExpensePaidAsync(ex, paid, paid == 0 ? "UNPAID" : "PARTIAL", ct);
            }
        await uow.Finance.DeleteAllocationsAsync(id, ct);
        await uow.Finance.ReverseEntryAsync(u.CompanyId, v.Get<long>("journalEntryId"), "VOUCHER_VOID", id, $"Void of voucher {v.Get<long>("voucherNo")}", ct);
        await uow.Finance.VoidVoucherAsync(id, b.Reason!.Trim(), u.UserId, ct);
        await uow.CommitAsync(ct);
    }

    public async Task<Row> VoucherAsync(UserContext u, long id, CancellationToken ct)
    {
        u.RequireAny(P.VoucherPost, P.PurchasePay, P.ExpenseRecord, P.ReportView, P.ReportAccounts);
        await using var uow = await uows.BeginAsync(u, ct);
        return await VoucherDetailAsync(uow, id, ct);
    }

    public async Task<Page<Row>> VouchersAsync(UserContext u, VoucherFilter f, PageQuery q, CancellationToken ct)
    {
        u.RequireAny(P.VoucherPost, P.PurchasePay, P.ExpenseRecord, P.ReportView, P.ReportAccounts);
        if (f.StoreId is { } s) u.RequireStore(s);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Finance.VouchersAsync(f, q, ct);
    }

    private static async Task<Row> VoucherDetailAsync(IUnitOfWork uow, long id, CancellationToken ct)
    {
        var v = await uow.Finance.VoucherAsync(id, ct) ?? throw new NotFoundException("Voucher", id);
        v["allocations"] = await uow.Finance.VoucherAllocationsAsync(id, ct);
        return v;
    }

    // ------------------------------------------------------------------------------------------ vendors

    public async Task<List<Row>> VendorSummaryAsync(UserContext u, CancellationToken ct)
    {
        u.RequireAny(P.PurchasePay, P.PurchaseCreate, P.VendorManage, P.ReportView);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Finance.VendorSummaryAsync(ct);
    }

    public async Task<List<Row>> OpenBillsAsync(UserContext u, long vendorId, CancellationToken ct)
    {
        u.RequireAny(P.PurchasePay, P.PurchaseCreate, P.VendorManage, P.ReportView);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Finance.OpenPurchaseBillsAsync(vendorId, ct);
    }

    public async Task<List<Row>> VendorDueAsync(UserContext u, CancellationToken ct)
    {
        u.RequireAny(P.PurchasePay, P.VendorManage, P.ReportView);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Finance.VendorDueAsync(ct);
    }

    /// <summary>A reminder to pay a vendor on a date. It never changes any balance.</summary>
    public async Task<long> PlanPaymentAsync(UserContext u, SchedulePlanBody b, CancellationToken ct)
    {
        u.RequireAny(P.PurchasePay, P.VendorManage);
        var store = b.StoreId ?? u.StoreId; u.RequireStore(store);
        new Check().Require(b.Amount > 0, "amount", "Amount must be above zero.").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        var id = await uow.Finance.InsertScheduleAsync(u.CompanyId, store, b.VendorId, b.PurchaseInvoiceId, b.DueDate, b.Amount, b.Note, ct);
        await uow.CommitAsync(ct);
        return id;
    }

    public async Task CancelPlannedPaymentAsync(UserContext u, long id, CancellationToken ct)
    {
        u.RequireAny(P.PurchasePay, P.VendorManage);
        await using var uow = await uows.BeginAsync(u, ct);
        if (!await uow.Finance.CancelScheduleAsync(id, ct)) throw new NotFoundException("Planned payment", id);
        await uow.CommitAsync(ct);
    }

    // ------------------------------------------------------------------------------------------ opening balances

    /// <summary>
    /// Opening balances: each item is a debit or credit on an account (a party's account needs the party). The difference is booked to
    /// "Opening balance equity", so the entry always balances. Allowed by OPENING_BALANCE.
    /// </summary>
    public async Task<Row> OpeningAsync(UserContext u, OpeningBody b, CancellationToken ct)
    {
        u.Require(P.OpeningBalance);
        var store = b.StoreId ?? u.StoreId; u.RequireStore(store);
        var items = b.Items ?? [];
        new Check().Require(items.Count > 0, "items", "Add at least one balance.").Require(items.All(i => (i.Debit > 0) != (i.Credit > 0)), "items", "Each line is either a debit or a credit, above zero.").ThrowIfAny();
        var date = b.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        await using var uow = await uows.BeginAsync(u, ct);
        var equity = await uow.Finance.RoleAccountAsync(0, "OPENING_EQUITY", ct);
        var control = await uow.Finance.RoleAccountAsync(0, "PARTY_CONTROL", ct);
        var entry = await uow.Finance.InsertJournalAsync(u.CompanyId, date, store, "OPENING", null, StoreService.Blank(b.Note) ?? "Opening balances", u.UserId, ct);
        var n = 0; long dr = 0, cr = 0;
        foreach (var i in items)
        {
            long account;
            if (i.PartyId is { } p) { await RequirePartyAsync(uow, p, "partyId", ct); account = control; }
            else account = i.AccountId ?? throw new ValidationException("items", "Each line needs an account or a party.");
            var a = await uow.Finance.AccountAsync(account, ct) ?? throw new ValidationException("items", $"Account {account} not found.");
            if (!a.Get<bool>("isPostable")) throw new BusinessRuleException($"Account {a.Get<string>("code")} does not accept postings.", "not_postable");
            await uow.Finance.AddLineAsync(u.CompanyId, entry, date, ++n, store, account, i.PartyId, i.Debit, i.Credit, "Opening balance", ct);
            dr += i.Debit; cr += i.Credit;
        }
        if (dr != cr) await uow.Finance.AddLineAsync(u.CompanyId, entry, date, ++n, store, equity, null, dr < cr ? cr - dr : 0, dr > cr ? dr - cr : 0, "Opening balance equity", ct);
        await uow.CommitAsync(ct);
        return new Row { ["entryId"] = entry, ["debit"] = Math.Max(dr, cr), ["credit"] = Math.Max(dr, cr), ["equityPlug"] = Math.Abs(dr - cr) };
    }
}

public sealed record VoidReason(string? Reason);
