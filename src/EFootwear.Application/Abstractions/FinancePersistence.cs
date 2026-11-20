namespace EFootwear.Application.Abstractions;

public sealed record VoucherInsert(long CompanyId, long StoreId, string Type, long VoucherNo, DateOnly Date, long DebitAccountId, long CreditAccountId, long Amount,
                                   long? PartyId, string? Mode, string? RefNo, string? Description, long JournalEntryId);

public sealed record VoucherFilter(long? StoreId, string? Type, DateOnly? From, DateOnly? To, long? PartyId, string? Status);

public sealed record ExpenseInsert(long CompanyId, long StoreId, long CategoryId, DateOnly Date, long Amount, long? PayeeId, string? BillRef, string? Description, DateOnly? DueDate, string? Method);

public sealed record ExpenseFilter(long? StoreId, string? Status, string? PaymentStatus, long? CategoryId, DateOnly? From, DateOnly? To);

public sealed record PlanInput(string Name, string AppliesTo, string Basis, string CalcType, decimal? DefaultRate, long? DefaultFixed, long MinStoreSales, long? StoreId, DateOnly ValidFrom, DateOnly? ValidTo, bool IsActive);

public sealed record CommissionRuleInput(string Scope, long? ProductId, long? CategoryId, long? PriceBandId, string CalcType, decimal? Rate, long? Fixed, long MinLineAmount, int Priority, DateOnly ValidFrom, DateOnly? ValidTo);

public interface IFinanceRepository
{
    // accounts and ledgers
    Task<List<Row>> AccountsAsync(bool includeInactive, CancellationToken ct);
    Task<Row?> AccountAsync(long id, CancellationToken ct);
    Task<long> InsertAccountAsync(long companyId, string code, string mainType, string group, string name, long? parentId, long storeId, bool postable, CancellationToken ct);
    Task<bool> UpdateAccountAsync(long id, string name, bool isActive, long? rowVer, CancellationToken ct);
    Task<long> RoleAccountAsync(long storeId, string role, CancellationToken ct);
    Task<List<Row>> PartyLedgerAsync(long partyId, DateOnly from, DateOnly to, CancellationToken ct);
    Task<long> PartyOpeningAsync(long partyId, DateOnly before, CancellationToken ct);
    Task<long> NextNumberAsync(long companyId, long storeId, string series, CancellationToken ct);
    Task<long> InsertJournalAsync(long companyId, DateOnly date, long storeId, string sourceType, long? sourceId, string description, long userId, CancellationToken ct);
    Task AddLineAsync(long companyId, long entryId, DateOnly date, int lineNo, long storeId, long accountId, long? partyId, long debit, long credit, string? description, CancellationToken ct);
    Task<long> ReverseEntryAsync(long companyId, long entryId, string sourceType, long sourceId, string description, CancellationToken ct);

    // vouchers
    Task<long> InsertVoucherAsync(VoucherInsert v, CancellationToken ct);
    Task<Row?> VoucherAsync(long id, CancellationToken ct);
    Task<List<Row>> VoucherAllocationsAsync(long voucherId, CancellationToken ct);
    Task<Page<Row>> VouchersAsync(VoucherFilter f, PageQuery q, CancellationToken ct);
    Task InsertAllocationAsync(long companyId, long voucherId, long storeId, long? purchaseInvoiceId, long? saleInvoiceId, long? expenseId, long amount, CancellationToken ct);
    Task DeleteAllocationsAsync(long voucherId, CancellationToken ct);
    Task VoidVoucherAsync(long id, string reason, long userId, CancellationToken ct);
    Task<Row?> PurchaseBalanceAsync(long invoiceId, CancellationToken ct);
    Task<List<Row>> OpenPurchaseBillsAsync(long vendorId, CancellationToken ct);
    Task<List<Row>> VendorSummaryAsync(CancellationToken ct);
    Task<List<Row>> VendorDueAsync(CancellationToken ct);
    Task<long> InsertScheduleAsync(long companyId, long storeId, long vendorId, long? invoiceId, DateOnly due, long amount, string? note, CancellationToken ct);
    Task<bool> CancelScheduleAsync(long id, CancellationToken ct);
    Task<Row?> ExpenseForPaymentAsync(long id, CancellationToken ct);
    Task SetExpensePaidAsync(long id, long paid, string status, CancellationToken ct);

    // expenses
    Task<List<Row>> ExpenseCategoriesAsync(CancellationToken ct);
    Task<long> InsertExpenseCategoryAsync(long companyId, string code, string name, long glAccountId, bool recurring, CancellationToken ct);
    Task<long> InsertExpenseAsync(ExpenseInsert e, CancellationToken ct);
    Task<bool> UpdateExpenseAsync(long id, ExpenseInsert e, long? rowVer, CancellationToken ct);
    Task<Row?> ExpenseAsync(long id, CancellationToken ct);
    Task<List<Row>> ExpenseAllocationsAsync(long id, CancellationToken ct);
    Task ReplaceExpenseAllocationsAsync(long companyId, long id, IReadOnlyCollection<(long StoreId, long Amount)> allocations, CancellationToken ct);
    Task<Page<Row>> ExpensesAsync(ExpenseFilter f, PageQuery q, CancellationToken ct);
    Task SubmitExpenseAsync(long companyId, long id, long userId, CancellationToken ct);
    Task<long> ApproveExpenseAsync(long companyId, long id, long userId, CancellationToken ct);
    Task RejectExpenseAsync(long companyId, long id, long userId, string reason, CancellationToken ct);
    Task<bool> DeleteDraftExpenseAsync(long id, CancellationToken ct);

    // commissions and payroll
    Task<List<Row>> PlansAsync(CancellationToken ct);
    Task<Row?> PlanAsync(long id, CancellationToken ct);
    Task<long> InsertPlanAsync(long companyId, PlanInput p, CancellationToken ct);
    Task<bool> UpdatePlanAsync(long id, PlanInput p, long? rowVer, CancellationToken ct);
    Task ReplacePlanMembersAsync(long companyId, long planId, IReadOnlyCollection<long> employees, CancellationToken ct);
    Task<long> InsertCommissionRuleAsync(long companyId, long planId, CommissionRuleInput r, CancellationToken ct);
    Task EndCommissionRuleAsync(long ruleId, DateOnly validTo, CancellationToken ct);
    Task<Page<Row>> CommissionEntriesAsync(long? storeId, long? employeeId, string? status, DateOnly? from, DateOnly? to, PageQuery q, CancellationToken ct);
    Task<List<Row>> CommissionSummaryAsync(long? storeId, DateOnly? month, CancellationToken ct);
    Task<int> ApproveCommissionsAsync(long companyId, long storeId, DateOnly from, DateOnly to, long userId, CancellationToken ct);
    Task<long> BuildPayrollAsync(long companyId, long storeId, DateOnly month, long userId, CancellationToken ct);
    Task<long> ApprovePayrollAsync(long companyId, long runId, long userId, CancellationToken ct);
    Task<List<Row>> PayrollRunsAsync(long? storeId, CancellationToken ct);
    Task<Row?> PayrollRunAsync(long id, CancellationToken ct);
    Task<List<Row>> PayrollLinesAsync(long runId, CancellationToken ct);
    Task<Row?> PayrollLineAsync(long id, CancellationToken ct);
    Task<long> InsertAdjustmentAsync(long companyId, long employeeId, DateOnly month, string type, long amount, string? reason, CancellationToken ct);
    Task<long> PaySalaryAsync(long companyId, long lineId, long amount, string method, DateOnly paidOn, long userId, string? reference, CancellationToken ct);
    Task<List<Row>> SalaryPaymentsAsync(long lineId, CancellationToken ct);
    Task<bool> CancelDraftRunAsync(long companyId, long id, CancellationToken ct);

    // cash
    Task<Page<Row>> PromisesAsync(long? storeId, long? customerId, string? status, PageQuery q, CancellationToken ct);
    Task<long> InsertPromiseAsync(long companyId, long storeId, long customerId, DateOnly due, long amount, string? note, CancellationToken ct);
    Task<Row?> PromiseAsync(long id, CancellationToken ct);
    Task UpdatePromiseAsync(long id, long paid, string status, CancellationToken ct);
    Task<long> ExpectedCashAsync(long storeId, DateOnly asOf, CancellationToken ct);
    Task<long?> LastCountedCashAsync(long storeId, DateOnly before, CancellationToken ct);
    Task<long> InsertCashCountAsync(long companyId, long storeId, DateOnly date, long opening, long expected, long counted, string? note, long userId, CancellationToken ct);
    Task InsertDenominationAsync(long companyId, long countId, int denomination, int notes, CancellationToken ct);
    Task<Row?> CashCountAsync(long id, CancellationToken ct);
    Task<List<Row>> CashCountsAsync(long? storeId, DateOnly? from, DateOnly? to, CancellationToken ct);

    // reports
    Task<List<Row>> RunReportAsync(string key, ReportArgs a, CancellationToken ct);
}

/// <summary>Everything a report may be filtered by. A report uses only the fields its definition names (see ReportCatalog).</summary>
public sealed record ReportArgs(long CompanyId, DateOnly From, DateOnly To, long? StoreId, long? AccountId, long? VendorId, long? CustomerId, long? CategoryId, string? DocType);
