namespace EFootwear.Domain.Security.Permissions;

/// <summary>
/// THE list of modules, screens and actions of the application: the single source of truth for permissions.
///
///  * The permission tree in the user interface is generated from it (GET /api/v1/permissions/catalog).
///  * sec.permissions is filled from it at startup (PermissionCatalogSynchronizer).
///  * PermissionCodes.generated.cs (the constants used by [RequirePermission]) is generated from it
///    (dotnet run --project E-Footwear-Api -- generate-permission-codes); a unit test fails when the file is stale.
///
/// Naming: the original menu (docx 1.2) is the base. Where the docx notes two names for one screen, ONE name is used:
/// Sale Invoice = New Sale, Purchase Invoice = New Purchase, Change Barcode = Replace Barcodes,
/// Send For Claim = Product Claim, Stock Adjustment (Excel) = Stock Adjustment (Non Zero Stock),
/// Vendor / Company Purchase Order = New Purchase Order Vendor / Company.
/// The Logout and Exit entries of the User Account menu are not permissions: everybody may sign out.
/// Modules after Settings come from Section B of PROJECT_REQUIREMENTS (vendors, expenses, payroll, commissions,
/// transfers, promotions, devices, synchronization, dashboards).
///
/// To add a screen: add one line here, run the generator command, and use the new constant. Never edit
/// PermissionCodes.generated.cs by hand.
/// </summary>
internal static class PermissionCatalogDefinitions
{
    public const string AccountReportsSubGroupCode = "ACCOUNT_REPORTS";
    public const string GlAccountSubGroupCode = "GL_ACCOUNT";

    public static IReadOnlyList<PermissionModuleDefinition> BuildModules()
    {
        var builder = new PermissionCatalogBuilder();

        builder.Module("USER_ACCOUNT", "User Account")
            .Screen("CREATE_USER", "Create New User", PermissionActionSets.Entry)
            .Screen("VIEW_USERS", "View Users", PermissionActionSets.List)
            .Screen("USERS_ACTIVITY", "Users Activity", PermissionActionSets.Report)
            .Screen("LOGIN_HISTORY", "Login History", PermissionActionSets.Report)
            .Screen("CHANGE_PASSWORD", "Change Password", PermissionActionSets.Operation)
            .Screen("ROLES_AND_PERMISSIONS", "Roles and Permissions", PermissionActionSets.Entry);

        builder.Module("PRODUCTS", "Products")
            .Screen("NEW_PRODUCT", "New Product", PermissionActionSets.EntryWithSellingPriceControl)
            .Screen("VIEW_PRODUCTS", "View Products", PermissionActionSets.List)
            .Screen("SEARCH_PRODUCT", "Search Product", PermissionActionSets.ViewOnly)
            .Screen("PRODUCT_LEDGER", "Product Ledger", PermissionActionSets.Report)
            .Screen("GENERATE_BARCODES", "Generate Barcodes", [PermissionActionCodes.View, PermissionActionCodes.Print])
            .Screen("PRICE_CHECK", "Price Check", PermissionActionSets.ViewOnly)
            .Screen("CHANGE_ATTRIBUTES", "Change Attributes", PermissionActionSets.Operation)
            .Screen("CHANGE_PRICES_OLD_TO_NEW", "Change Prices (Old to New)", PermissionActionSets.Operation)
            .Screen("CHANGE_PRICES_BY_PERCENTAGE", "Change Prices (In %)", PermissionActionSets.Operation)
            .Screen("CHANGE_DISCOUNTS", "Change Discounts", PermissionActionSets.Operation)
            .Screen("PRODUCT_DISCOUNTS", "Product Discounts", PermissionActionSets.Operation)
            .Screen("PRODUCT_DISCOUNTS_ON_MULTIPLE_STORES", "Product Discounts on Multiple Stores", PermissionActionSets.Operation)
            .Screen("REMOVE_DISCOUNTS_AND_REVERSE_PRICE", "Remove Discounts and Reverse Price", PermissionActionSets.Operation)
            .Screen("REPLACE_BARCODES", "Replace Barcodes", PermissionActionSets.Operation)
            .Screen("PRODUCTS_STOCK_WITH_SIZES_ON_MULTIPLE_STORES", "Products Stock with Sizes on Multiple Stores", PermissionActionSets.Report)
            .Screen("PRODUCT_CLAIM", "Product Claim", PermissionActionSets.Document)
            .Screen("CLAIM_APPROVAL", "Claim Approval", PermissionActionSets.Approval)
            .Screen("PRODUCT_REPORT", "Product Report", PermissionActionSets.Report)
            .Screen("PRICE_BANDS", "Price Bands", PermissionActionSets.Entry);

        builder.Module("PARTY", "Party")
            .Screen("NEW_PARTY", "New Party", PermissionActionSets.Entry)
            .Screen("VIEW_PARTY", "View Party", PermissionActionSets.List)
            .Screen("SEARCH_PARTY", "Search Party", PermissionActionSets.ViewOnly)
            .Screen("ACCOUNT_DETAIL", "Account Detail", PermissionActionSets.List)
            .Screen("PROMISE", "Promise", PermissionActionSets.Entry)
            .Screen("CASH_CALCULATOR", "Cash Calculator", PermissionActionSets.Entry)
            .Screen("VIEW_CASH_DETAIL", "View Cash Detail", PermissionActionSets.List)
            .Screen("NEW_SALARY_SHEET", "New Salary Sheet", PermissionActionSets.Entry)
            .Screen("VIEW_SALARY_SHEET", "View Salary Sheet", PermissionActionSets.List);

        builder.Module("INVOICE", "Invoice")
            .Screen("NEW_SALE", "New Sale", PermissionActionSets.Sales)
            .Screen("SALE_RETURN", "Sale Return", PermissionActionSets.Entry)
            .Screen("EXCHANGE_INVOICE", "Exchange Invoice", PermissionActionSets.Sales)
            .Screen("NEW_PURCHASE", "New Purchase", PermissionActionSets.Entry)
            .Screen("PURCHASE_RETURN", "Purchase Return", PermissionActionSets.Entry)
            .Screen("PURCHASE_ORDER", "Purchase Order", PermissionActionSets.Report)
            .Screen("STOCK_ADJUSTMENT", "Stock Adjustment", PermissionActionSets.EntryWithApproval)
            .Screen("STOCK_ADJUSTMENT_NON_ZERO_STOCK", "Stock Adjustment (Non Zero Stock)", PermissionActionSets.EntryWithApproval)
            .Screen("STOCK_TRANSFER", "Stock Transfer", PermissionActionSets.Entry)
            .Screen("STOCK_TRANSFER_MULTI", "Stock Transfer Multi", PermissionActionSets.Entry)
            .Screen("STOCK_TRANSFER_OF_PURCHASE_INVOICE", "Stock Transfer of Purchase Invoice", PermissionActionSets.Entry)
            .Screen("STOCK_AUDIT", "Stock Audit", PermissionActionSets.EntryWithApproval)
            .Screen("VIEW_INVOICES", "View Invoices", PermissionActionSets.List)
            .Screen("VIEW_DELETED_INVOICES", "View Deleted Invoices", PermissionActionSets.Report);

        builder.Module("ORDERS", "Orders")
            .Screen("NEW_SALE_ORDER", "New Sale Order", PermissionActionSets.Entry)
            .Screen("SALE_RETURN_ORDER", "Sale Return Order", PermissionActionSets.Entry)
            .Screen("NEW_PURCHASE_ORDER", "New Purchase Order", PermissionActionSets.Entry)
            .Screen("PURCHASE_RETURN_ORDER", "Purchase Return Order", PermissionActionSets.Entry)
            .Screen("VENDOR_PURCHASE_ORDER", "Vendor Purchase Order", PermissionActionSets.Entry)
            .Screen("COMPANY_PURCHASE_ORDER", "Company Purchase Order", PermissionActionSets.Entry)
            .Screen("VIEW_ORDERS", "View Orders", PermissionActionSets.List);

        builder.Module("VOUCHER", "Voucher")
            .Screen("PAYMENT_VOUCHER", "Payment Voucher", PermissionActionSets.EntryWithApproval)
            .Screen("RECEIPT_VOUCHER", "Receipt Voucher", PermissionActionSets.EntryWithApproval)
            .Screen("EXPENSE_VOUCHER", "Expense Voucher", PermissionActionSets.EntryWithApproval)
            .Screen("JOURNAL_VOUCHER", "Journal Voucher", PermissionActionSets.EntryWithApproval)
            .Screen("VIEW_VOUCHER", "View Voucher", PermissionActionSets.List)
            .Screen("VOUCHER_REPORT", "Voucher Report", PermissionActionSets.Report)
            // Part 15: accounting periods (closing a month) and the cash and bank balances of cash management.
            .Screen("ACCOUNTING_PERIODS", "Accounting Periods", PermissionActionSets.Approval)
            .Screen("CASH_AND_BANK_BALANCES", "Cash and Bank Balances", PermissionActionSets.Report);

        builder.Module("REPORTS", "Reports")
            .Screen("ACCOUNT_LEDGER", "Account Ledger", PermissionActionSets.Report, PermissionCatalogDefinitions.AccountReportsSubGroupCode)
            .Screen("ACCOUNT_BALANCE", "Account Balance", PermissionActionSets.Report, PermissionCatalogDefinitions.AccountReportsSubGroupCode)
            .Screen("TRIAL_BALANCE", "Trial Balance", PermissionActionSets.Report, PermissionCatalogDefinitions.AccountReportsSubGroupCode)
            .Screen("DETAIL_TRIAL_BALANCE", "Detail Trial Balance", PermissionActionSets.Report, PermissionCatalogDefinitions.AccountReportsSubGroupCode)
            .Screen("PURCHASE_REPORT", "Purchase Report", PermissionActionSets.Report)
            .Screen("SALE_REPORT", "Sale Report", PermissionActionSets.Report)
            .Screen("SALE_REPORT_CONSOLIDATED", "Sale Report (Consolidated)", PermissionActionSets.Report)
            .Screen("SALE_REPORT_CATEGORY_COUNT", "Sale Report (Category Count)", PermissionActionSets.Report)
            .Screen("SALE_REPORT_PRICE_WISE", "Sale Report (Price Wise)", PermissionActionSets.Report)
            .Screen("SALE_REPORT_PRICE_WISE_DETAIL", "Sale Report (Price Wise Detail)", PermissionActionSets.Report)
            .Screen("SALE_VALUE_REPORT", "Sale Value Report", PermissionActionSets.Report)
            .Screen("AGING_REPORT", "Aging Report", PermissionActionSets.Report)
            .Screen("PROFIT_REPORT", "Profit Report", PermissionActionSets.Report)
            .Screen("COMMISSION_REPORT", "Commission Report", PermissionActionSets.Report)
            .Screen("CUT_SIZE_REPORT", "Cut Size Report", PermissionActionSets.Report)
            .Screen("STOCK_REPORT_CONSOLIDATED", "Stock Report (Consolidated)", PermissionActionSets.Report)
            .Screen("STOCK_REPORT_CATEGORY_COUNT", "Stock Report (Category Count)", PermissionActionSets.Report)
            .Screen("STOCK_QUANTITY_REPORT", "Stock Quantity Report", PermissionActionSets.Report)
            .Screen("STOCK_VALUE_REPORT", "Stock Value Report", PermissionActionSets.Report)
            .Screen("STOCK_ADJUSTMENT_REPORT", "Stock Adjustment Report", PermissionActionSets.Report)
            .Screen("STOCK_TRANSFER_REPORT", "Stock Transfer Report", PermissionActionSets.Report)
            .Screen("STOCK_TRANSFER_SUMMARY", "Stock Transfer Summary", PermissionActionSets.Report)
            .Screen("STOCK_TURN_REPORT", "Stock Turn Report", PermissionActionSets.Report)
            .Screen("STOCK_AUDIT_REPORT", "Stock Audit Report", PermissionActionSets.Report)
            .Screen("SALE_AND_STOCK_REPORT", "Sale and Stock Report", PermissionActionSets.Report)
            .Screen("AREA_WISE_BALANCE_REPORT", "Area Wise Balance Report", PermissionActionSets.Report)
            .Screen("DEAD_ITEMS_REPORT", "Dead Items Report", PermissionActionSets.Report)
            .Screen("GENERAL_JOURNAL", "General Journal", PermissionActionSets.Report)
            .Screen("BUSINESS_REPORT", "Business Report", PermissionActionSets.Report)
            .Screen("BUSINESS_OVERVIEW", "Business Overview", PermissionActionSets.Report)
            .Screen("CLAIM_REPORT", "Claim Report", PermissionActionSets.Report)
            .Screen("VENDOR_BALANCE_AND_AGING_REPORT", "Vendor Balance and Aging Report", PermissionActionSets.Report)
            .Screen("VENDOR_PAYMENT_SCHEDULE_REPORT", "Vendor Payment Schedule Report", PermissionActionSets.Report)
            .Screen("VENDOR_PURCHASE_HISTORY_REPORT", "Vendor Purchase History Report", PermissionActionSets.Report)
            .Screen("VENDOR_PRODUCT_MAPPING_REPORT", "Vendor Product Mapping Report", PermissionActionSets.Report)
            .Screen("STAFF_SALARY_REPORT", "Staff Salary Report", PermissionActionSets.Report)
            .Screen("SALARY_OUTSTANDING_REPORT", "Salary Outstanding Report", PermissionActionSets.Report)
            .Screen("COMMISSION_ACCRUAL_AND_PAYMENT_REPORT", "Commission Accrual and Payment Report", PermissionActionSets.Report)
            .Screen("STORE_EXPENSES_REPORT", "Store Expenses Report", PermissionActionSets.Report)
            .Screen("COMPANY_EXPENSE_ALLOCATION_REPORT", "Company Expense Allocation Report", PermissionActionSets.Report)
            .Screen("PROMOTION_PERFORMANCE_REPORT", "Promotion Performance Report", PermissionActionSets.Report)
            .Screen("OFFLINE_SYNCHRONIZATION_STATUS_REPORT", "Offline Synchronization Status Report", PermissionActionSets.Report)
            .Screen("OFFLINE_TRANSACTION_RECONCILIATION_REPORT", "Offline Transaction Reconciliation Report", PermissionActionSets.Report)
            .Screen("STOCK_TRANSFER_DISCREPANCIES_REPORT", "Stock Transfer Discrepancies Report", PermissionActionSets.Report)
            .Screen("PRODUCT_PERFORMANCE_BY_STORE_REPORT", "Product Performance by Store Report", PermissionActionSets.Report)
            .Screen("PRICE_BAND_PERFORMANCE_REPORT", "Price Band Performance Report", PermissionActionSets.Report)
            .Screen("SLOW_MOVING_STOCK_REPORT", "Slow-Moving Stock Report", PermissionActionSets.Report)
            .Screen("DEAD_STOCK_REPORT", "Dead Stock Report", PermissionActionSets.Report)
            .Screen("TRANSFER_OPPORTUNITY_ANALYSIS_REPORT", "Transfer Opportunity Analysis Report", PermissionActionSets.Report);

        builder.Module("SETTINGS", "Settings")
            .Screen("GL_ACCOUNTS", "GL Accounts", PermissionActionSets.Entry, PermissionCatalogDefinitions.GlAccountSubGroupCode)
            .Screen("NEW_SECOND_LEVEL", "New Second Level", PermissionActionSets.Entry, PermissionCatalogDefinitions.GlAccountSubGroupCode)
            .Screen("VIEW_SECOND_LEVEL", "View Second Level", PermissionActionSets.List, PermissionCatalogDefinitions.GlAccountSubGroupCode)
            .Screen("DEPARTMENT", "Department", PermissionActionSets.Entry)
            .Screen("CATEGORY", "Category", PermissionActionSets.Entry)
            .Screen("COMPANY_BRAND", "Company / Brand", PermissionActionSets.Entry)
            .Screen("SEASON", "Season", PermissionActionSets.Entry)
            .Screen("GENDER", "Gender", PermissionActionSets.Entry)
            .Screen("COLOR", "Color", PermissionActionSets.Entry)
            .Screen("LOCATION", "Location", PermissionActionSets.Entry)
            .Screen("UNIT", "Unit", PermissionActionSets.Entry)
            .Screen("SIZE_GROUP", "Size Group", PermissionActionSets.Entry)
            .Screen("PRODUCT_TYPE", "Product Type", PermissionActionSets.Entry)
            .Screen("DEALS", "Deals", PermissionActionSets.Entry)
            .Screen("SPECIAL_OFFERS", "Special Offers", PermissionActionSets.Entry)
            .Screen("SPECIAL_OFFER_DETAILS", "Special Offer Details", PermissionActionSets.Entry)
            .Screen("SALE_ON_STORES", "Sale on Stores", PermissionActionSets.Entry)
            .Screen("SALE_ON_CATEGORIES", "Sale on Categories", PermissionActionSets.Entry)
            .Screen("CITY_AREA", "City / Area", PermissionActionSets.Entry)
            .Screen("OPENING_BALANCE", "Opening Balance", PermissionActionSets.Entry)
            .Screen("PREFERENCE", "Preference", PermissionActionSets.Operation)
            .Screen("BACKUP", "Backup", [PermissionActionCodes.View, PermissionActionCodes.Add])
            .Screen("RESTORE", "Restore", PermissionActionSets.Operation)
            .Screen("COMPANY_PROFILE", "Company Profile", PermissionActionSets.Operation)
            .Screen("STORE_MANAGEMENT", "Store Management", PermissionActionSets.Entry)
            .Screen("DEVICES_AND_POS_TERMINALS", "Devices and POS Terminals", PermissionActionSets.EntryWithApproval)
            .Screen("DOCUMENT_NUMBERING", "Document Numbering", PermissionActionSets.Operation)
            .Screen("PAYMENT_METHODS", "Payment Methods", PermissionActionSets.Operation)
            .Screen("SIZES_AND_SIZE_GROUPS", "Sizes and Size Groups", PermissionActionSets.Entry)
            .Screen("PRODUCT_TYPES", "Product Types", PermissionActionSets.Entry);

        builder.Module("INVENTORY", "Inventory")
            .Screen("STOCK_BALANCES", "Stock Balances", PermissionActionSets.Report)
            .Screen("STOCK_LEDGER", "Stock Ledger", PermissionActionSets.Report)
            .Screen("STOCK_RECONCILIATION", "Stock Reconciliation", [PermissionActionCodes.View, PermissionActionCodes.Edit, PermissionActionCodes.Approve])
            .Screen("STOCK_TRANSFER_RECEIVING", "Receive Stock Transfer", PermissionActionSets.Entry)
            .Screen("TRANSFER_DISCREPANCIES", "Transfer Discrepancies",
                    [PermissionActionCodes.View, PermissionActionCodes.Edit, PermissionActionCodes.Approve, PermissionActionCodes.Print, PermissionActionCodes.Export])
            .Screen("STOCK_EXCEPTIONS", "Stock Exceptions", PermissionActionSets.Approval);

        builder.Module("VENDORS", "Vendors")
            .Screen("VENDOR_MANAGEMENT", "Vendor Management", PermissionActionSets.Entry)
            .Screen("VENDOR_PAYMENT_SCHEDULE", "Vendor Payment Schedule", PermissionActionSets.Entry)
            .Screen("VENDOR_PRODUCT_MAPPING", "Vendor Product Mapping", PermissionActionSets.Entry);

        builder.Module("EXPENSES", "Expenses")
            .Screen("EXPENSE_CATEGORIES", "Expense Categories", PermissionActionSets.Entry)
            .Screen("EXPENSE_RECORDING", "Expenses", PermissionActionSets.EntryWithApproval)
            .Screen("EXPENSE_ALLOCATIONS", "Expense Allocations", PermissionActionSets.Entry)
            .Screen("RECURRING_EXPENSES", "Recurring Expenses", PermissionActionSets.Entry);

        builder.Module("HUMAN_RESOURCES", "Human Resources")
            .Screen("STAFF_PROFILES", "Staff Profiles", PermissionActionSets.Entry)
            .Screen("STAFF_TRANSFERS", "Staff Transfers", PermissionActionSets.Document)
            .Screen("SALARY_TERMS", "Salary Terms", PermissionActionSets.Entry)
            .Screen("PAYROLL_RUNS", "Payroll Runs", PermissionActionSets.EntryWithApproval)
            .Screen("SALARY_PAYMENTS", "Salary Payments", [PermissionActionCodes.View, PermissionActionCodes.Add, PermissionActionCodes.Print, PermissionActionCodes.Export]);

        builder.Module("COMMISSIONS", "Commissions")
            .Screen("COMMISSION_PLANS", "Commission Plans", PermissionActionSets.Entry)
            .Screen("COMMISSION_ENTRIES", "Commission Entries", [PermissionActionCodes.View, PermissionActionCodes.Approve, PermissionActionCodes.Print, PermissionActionCodes.Export]);

        builder.Module("PROMOTIONS", "Promotions")
            .Screen("PROMOTION_RULES", "Promotion Rules", PermissionActionSets.Entry)
            .Screen("PROMOTION_BUNDLES", "Promotion Bundles", PermissionActionSets.Entry)
            .Screen("PROMOTION_CONFLICT_RESOLUTION", "Promotion Conflict Resolution", PermissionActionSets.Approval);

        builder.Module("DASHBOARDS", "Dashboards")
            .Screen("OWNER_DASHBOARD", "Company Owner Dashboard", PermissionActionSets.ViewOnly)
            .Screen("STORE_MANAGER_DASHBOARD", "Store Manager Dashboard", PermissionActionSets.ViewOnly)
            .Screen("ACCOUNTANT_DASHBOARD", "Accountant Dashboard", PermissionActionSets.ViewOnly)
            .Screen("INVENTORY_MANAGER_DASHBOARD", "Inventory Manager Dashboard", PermissionActionSets.ViewOnly)
            .Screen("PRODUCT_PERFORMANCE_ANALYTICS", "Product Performance Analytics", PermissionActionSets.Report)
            .Screen("PRICE_BAND_ANALYTICS", "Price Band Analytics", PermissionActionSets.Report)
            .Screen("TRANSFER_OPPORTUNITIES", "Stock Transfer Opportunities", PermissionActionSets.Report);

        builder.Module("OFFLINE_SYNCHRONIZATION", "Offline Synchronization")
            .Screen("SYNCHRONIZATION_MONITORING", "Synchronization Monitoring", [PermissionActionCodes.View, PermissionActionCodes.Export])
            .Screen("SYNCHRONIZATION_CONFLICTS", "Synchronization Conflicts", PermissionActionSets.Approval);

        builder.Module("INTEGRATIONS", "Integrations")
            .Screen("SHOPIFY_INTEGRATION", "Shopify Integration", PermissionActionSets.Operation);

        // The legacy codes. The SQL functions call sec.require_permission with exactly these codes, so they stay
        // in sec.permissions and in the catalog. Descriptions are the ones already stored in the schema.
        builder.Module("RESTRICTED_OPERATIONS", "Restricted Operations")
            .RestrictedOperationGroup("SALES", "Sales",
                ("SALE_CREATE", "Create sales"),
                ("SALE_RETURN", "Process returns and exchanges"),
                ("PRICE_CHANGE", "Manual price change on a sale"),
                ("DISCOUNT_MANUAL", "Manual discount on a sale"),
                ("DISCOUNT_APPROVE", "Approve manual price or discount"))
            .RestrictedOperationGroup("PROMOTIONS", "Promotions",
                ("PROMO_MANAGE", "Create and end promotions"),
                ("PROMO_CONFLICT_RESOLVE", "Choose which overlapping discount applies"))
            .RestrictedOperationGroup("PURCHASES", "Purchases and Vendors",
                ("PURCHASE_CREATE", "Record purchases"),
                ("PURCHASE_PAY", "Pay vendors"),
                ("VENDOR_MANAGE", "Manage vendors and vendor articles"))
            .RestrictedOperationGroup("STOCK", "Stock and Transfers",
                ("STOCK_ADJUST", "Stock adjustments"),
                ("STOCK_AUDIT", "Stock audits"),
                ("TRANSFER_CREATE", "Create and ship transfers"),
                ("TRANSFER_RECEIVE", "Receive transfers"),
                ("TRANSFER_LOSS_DECIDE", "Assign or write off transfer losses"),
                ("CLAIM_REQUEST", "Raise product claims"),
                ("CLAIM_APPROVE", "Approve product claims"),
                ("STOCK_EXCEPTION_RESOLVE", "Resolve stock shortfalls from offline and Shopify sales"))
            .RestrictedOperationGroup("ACCOUNTING", "Accounting and Cash",
                ("VOUCHER_POST", "Post vouchers"),
                ("VOUCHER_DELETE", "Void vouchers"),
                ("OPENING_BALANCE", "Enter opening balances"),
                ("CASH_COUNT", "End-of-day cash count"))
            .RestrictedOperationGroup("EXPENSES_AND_PAYROLL", "Expenses, Payroll and Commissions",
                ("EXPENSE_RECORD", "Record expenses"),
                ("EXPENSE_APPROVE", "Approve expenses"),
                ("PAYROLL_PREPARE", "Prepare salary sheets"),
                ("PAYROLL_APPROVE", "Approve and pay salaries"),
                ("COMMISSION_VIEW", "View commissions"),
                ("COMMISSION_APPROVE", "Approve commissions"))
            .RestrictedOperationGroup("REPORTING", "Reporting",
                ("REPORT_VIEW", "View reports"),
                ("REPORT_ACCOUNTS", "Accounting reports: ledger, trial balance, journal"),
                ("REPORT_BUILDER", "Build and share custom reports"))
            .RestrictedOperationGroup("ADMINISTRATION", "Administration",
                ("MASTER_EDIT", "Edit master lists and products"),
                ("USER_ADMIN", "Manage users and roles"),
                ("USER_PASSWORD_RESET", "Reset another user's password (temporary password, forced change at next login)"),
                ("USER_BLOCK", "Block or unblock a user account"),
                ("LOGIN_HISTORY_VIEW", "View the login history of other users"),
                ("SHOPIFY_ADMIN", "Connect and manage Shopify"),
                ("SYNC_CONFLICT_RESOLVE", "Decide offline sync conflicts (accept by hand or reject, with a note)"));

        // <permission-catalog-modules:end> (add new modules above this line)
        return builder.Build();
    }
}
