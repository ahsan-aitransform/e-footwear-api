namespace EFootwear.Domain.Security;

/// <summary>
/// Permission codes of sec.permissions (v6). The company owner (role COMPANY_OWNER) holds all of them; every other user
/// holds exactly the codes in sec.user_permissions. The API checks them on every call (Part 6, Part 34 rule 14).
/// </summary>
public static class P
{
    public const string SaleCreate = "SALE_CREATE";
    public const string SaleReturn = "SALE_RETURN";
    public const string PriceChange = "PRICE_CHANGE";
    public const string DiscountManual = "DISCOUNT_MANUAL";
    public const string DiscountApprove = "DISCOUNT_APPROVE";
    public const string PromoManage = "PROMO_MANAGE";
    public const string PromoConflictResolve = "PROMO_CONFLICT_RESOLVE";
    public const string PurchaseCreate = "PURCHASE_CREATE";
    public const string PurchasePay = "PURCHASE_PAY";
    public const string StockAdjust = "STOCK_ADJUST";
    public const string StockAudit = "STOCK_AUDIT";
    public const string TransferCreate = "TRANSFER_CREATE";
    public const string TransferReceive = "TRANSFER_RECEIVE";
    public const string TransferLossDecide = "TRANSFER_LOSS_DECIDE";
    public const string VoucherPost = "VOUCHER_POST";
    public const string VoucherDelete = "VOUCHER_DELETE";
    public const string ExpenseRecord = "EXPENSE_RECORD";
    public const string ExpenseApprove = "EXPENSE_APPROVE";
    public const string PayrollPrepare = "PAYROLL_PREPARE";
    public const string PayrollApprove = "PAYROLL_APPROVE";
    public const string CommissionView = "COMMISSION_VIEW";
    public const string CommissionApprove = "COMMISSION_APPROVE";
    public const string CashCount = "CASH_COUNT";
    public const string ClaimApprove = "CLAIM_APPROVE";
    public const string ClaimRequest = "CLAIM_REQUEST";
    public const string OpeningBalance = "OPENING_BALANCE";
    public const string ReportView = "REPORT_VIEW";
    public const string ReportAccounts = "REPORT_ACCOUNTS";
    public const string ReportBuilder = "REPORT_BUILDER";
    public const string MasterEdit = "MASTER_EDIT";
    public const string UserAdmin = "USER_ADMIN";
    public const string ShopifyAdmin = "SHOPIFY_ADMIN";
    public const string VendorManage = "VENDOR_MANAGE";
    public const string UserPasswordReset = "USER_PASSWORD_RESET";
    public const string UserBlock = "USER_BLOCK";
    public const string LoginHistoryView = "LOGIN_HISTORY_VIEW";
    public const string SyncConflictResolve = "SYNC_CONFLICT_RESOLVE";
}

/// <summary>
/// Starting permission set per role when a user is created (editable per user afterwards — a template must not prevent
/// individual overrides, Part 6). Suggestions, not approved policy.
/// </summary>
public static class RoleTemplates
{
    public const string Owner = "COMPANY_OWNER";
    public const string Manager = "STORE_MANAGER";
    public const string Cashier = "CASHIER_SALESMAN";
    public const string Accountant = "ACCOUNTANT";
    public const string PlatformAdmin = "PLATFORM_ADMIN";

    public static readonly string[] Roles = [Owner, Manager, Cashier, Accountant, PlatformAdmin];

    public static IReadOnlyList<string> For(string role, IReadOnlyCollection<string> all) => role switch
    {
        Owner => [],   // the owner holds every permission implicitly (sec.require_permission)
        Manager => all.Where(p => p is not (P.UserAdmin or P.UserPasswordReset or P.UserBlock or P.ShopifyAdmin or P.OpeningBalance or P.LoginHistoryView)).ToList(),
        Cashier => [P.SaleCreate, P.SaleReturn, P.CashCount, P.ClaimRequest],
        Accountant => [P.VoucherPost, P.VoucherDelete, P.ExpenseRecord, P.ExpenseApprove, P.PurchasePay, P.OpeningBalance, P.ReportView, P.CommissionView, P.PayrollPrepare],
        _ => [],
    };
}
