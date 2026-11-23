namespace E_Footwear_Api.Routing;

/// <summary>
/// Every URL of the API, in one place. Controllers use these constants in [Route] and [HttpGet] attributes; nobody types a URL
/// string inside a controller. The version is part of the path (api/v1); a breaking change would add a v2 root next to it.
///
/// To add endpoints for a module: add a nested static class with its Root and the relative segments of its actions.
/// </summary>
public static class ApiRoutes
{
    public const string VersionPrefix = "api/v1";
    public const string HealthCheck = "/health";
    public const string OpenApiDocument = "/openapi/v1.json";
    public const string SwaggerRoutePrefix = "swagger";

    public static class Authentication
    {
        public const string Root = VersionPrefix + "/auth";
        public const string Login = "login";
        public const string Logout = "logout";
        public const string ChangePassword = "change-password";
        public const string CurrentSession = "me";
        public const string RequestPasswordReset = "password-reset/request";
        public const string CompletePasswordReset = "password-reset/complete";
    }

    public static class PlatformAuthentication
    {
        public const string Root = VersionPrefix + "/platform/auth";
        public const string Login = "login";
        public const string Logout = "logout";
    }

    public static class Platform
    {
        public const string Root = VersionPrefix + "/platform";
        public const string Companies = "companies";
        public const string CompanyStores = "companies/{companyId:guid}/stores";
    }

    public static class Stores
    {
        public const string Root = VersionPrefix + "/stores";
    }

    public static class Users
    {
        public const string Root = VersionPrefix + "/users";
        public const string CreationOptions = "creation-options";
        public const string ById = "{userId:guid}";
        public const string Block = "{userId:guid}/block";
        public const string Unblock = "{userId:guid}/unblock";
        public const string ResetPassword = "{userId:guid}/reset-password";
        public const string PasswordResetToken = "{userId:guid}/password-reset-token";
        public const string Permissions = "{userId:guid}/permissions";
        public const string Sessions = "{userId:guid}/sessions";
        public const string SessionById = "{userId:guid}/sessions/{sessionId:guid}";
        public const string QuickActions = "{userId:guid}/quick-actions";
        public const string Activity = "activity";
        public const string LoginHistory = "login-history";
    }

    /// <summary>The signed-in person's own account: sessions, sign-in history, quick-action toolbar. Needs no permission.</summary>
    public static class Account
    {
        public const string Root = VersionPrefix + "/account";
        public const string Sessions = "sessions";
        public const string OtherSessions = "sessions/others";
        public const string SessionById = "sessions/{sessionId:guid}";
        public const string LoginHistory = "login-history";
        public const string QuickActions = "quick-actions";
    }

    public static class RoleTemplates
    {
        public const string Root = VersionPrefix + "/role-templates";
        public const string ByKey = "{templateKey}";
        public const string ById = "{templateId:guid}";
    }

    public static class Devices
    {
        public const string Root = VersionPrefix + "/devices";
        public const string ById = "{deviceId:guid}";
        public const string Authorize = "{deviceId:guid}/authorize";
        public const string Revoke = "{deviceId:guid}/revoke";
        public const string RotateToken = "{deviceId:guid}/rotate-token";
    }

    public static class Permissions
    {
        public const string Root = VersionPrefix + "/permissions";
        public const string Catalog = "catalog";
    }

    /// <summary>Company settings sections, company profile and logo (Settings module).</summary>
    public static class CompanySettings
    {
        public const string Root = VersionPrefix + "/settings";
        public const string BusinessInfo = "business-info";
        public const string RegionalPreferences = "regional-preferences";
        public const string Printing = "printing";
        public const string Tax = "tax";
        public const string InventoryPolicy = "inventory-policy";
        public const string PromotionPolicy = "promotion-policy";
        public const string PaymentMethods = "payment-methods";
        public const string EnabledPaymentMethods = "payment-methods/enabled";
        public const string StoreBusinessInfo = "stores/{storeId:guid}/business-info";
        public const string StorePrinting = "stores/{storeId:guid}/printing";
        public const string CompanyProfile = "company-profile";
        public const string CompanyLogo = "company-logo";
        public const string CompanyLogoInformation = "company-logo/information";
    }

    public static class StoreManagement
    {
        public const string Root = VersionPrefix + "/store-management";
        public const string Detail = "{storeId:guid}";
        public const string Activate = "{storeId:guid}/activate";
        public const string Deactivate = "{storeId:guid}/deactivate";
        public const string PlanUsage = "plan-usage";
    }

    public static class DocumentNumbering
    {
        public const string Root = VersionPrefix + "/document-numbering";
    }

    public static class PosTerminals
    {
        public const string Root = VersionPrefix + "/pos-terminals";
    }

    public static class UserPreferences
    {
        public const string Root = VersionPrefix + "/my-preferences";
    }

    public static class Backup
    {
        public const string Root = VersionPrefix + "/backup";
        public const string Overview = "overview";
        public const string RestoreOverview = "restore-overview";
    }

    public static class PlatformPlans
    {
        public const string Root = VersionPrefix + "/platform/plans";
        public const string Detail = "{planCode}";
    }

    public static class PlatformCompanyAdministration
    {
        public const string Root = VersionPrefix + "/platform/companies";
        public const string Detail = "{companyId:guid}";
        public const string Plan = "{companyId:guid}/plan";
        public const string Status = "{companyId:guid}/status";
    }

    public static class PlatformAuditLog
    {
        public const string Root = VersionPrefix + "/platform/audit-log";
    }

    public static class PlatformBackup
    {
        public const string Root = VersionPrefix + "/platform/backup";
        public const string Overview = "overview";
    }

    /// <summary>Master data (module masterdata-parties): the simple master lists, categories and the sizes of a size group.</summary>
    public static class MasterData
    {
        public const string Departments = VersionPrefix + "/departments";
        public const string Brands = VersionPrefix + "/brands";
        public const string Seasons = VersionPrefix + "/seasons";
        public const string Genders = VersionPrefix + "/genders";
        public const string Colors = VersionPrefix + "/colors";
        public const string Locations = VersionPrefix + "/locations";
        public const string Units = VersionPrefix + "/units";
        public const string Cities = VersionPrefix + "/cities";
        public const string SizeGroups = VersionPrefix + "/size-groups";
        public const string ProductTypes = VersionPrefix + "/product-types";
        public const string Categories = VersionPrefix + "/categories";

        public const string Options = "options";
        public const string ItemById = "{itemId:guid}";
        public const string SizesOfSizeGroup = "{sizeGroupId:guid}/sizes";
        public const string SizeById = "{sizeGroupId:guid}/sizes/{sizeId:guid}";
    }

    /// <summary>Chart of accounts master data (docx 2.1 to 2.3): main types, second-level groups and accounts.</summary>
    public static class ChartOfAccounts
    {
        public const string Root = VersionPrefix + "/chart-of-accounts";
        public const string MainTypes = "main-types";
        public const string Groups = "groups";
        public const string NextGroupCode = "groups/next-code";
        public const string GroupByCode = "groups/{groupCode}";
        public const string Accounts = "accounts";
        public const string NextAccountCode = "accounts/next-code";
        public const string AccountById = "accounts/{accountId:guid}";
    }

    /// <summary>The Opening Balance screen (docx 2.18).</summary>
    public static class OpeningBalances
    {
        public const string Root = VersionPrefix + "/opening-balances";
    }

    /// <summary>Parties (docx 3.1 to 3.3), staff transfers and the party account detail (docx 3.4).</summary>
    public static class Parties
    {
        public const string Root = VersionPrefix + "/parties";
        public const string Search = "search";
        public const string Lookup = "lookup";
        public const string PartyById = "{partyId:guid}";
        public const string StaffTransfers = "{partyId:guid}/staff-transfers";
    }

    public static class PartyLedger
    {
        public const string Root = VersionPrefix + "/party-ledger";
    }

    /// <summary>Customer promises (docx 3.5).</summary>
    public static class Promises
    {
        public const string Root = VersionPrefix + "/promises";
        public const string PromiseById = "{promiseId:guid}";
        public const string CustomerBalance = "customers/{customerPartyId:guid}/balance";
    }

    /// <summary>End-of-day cash counts: the Cash Calculator and View Cash Detail (docx 3.6, 3.7).</summary>
    public static class CashCounts
    {
        public const string Root = VersionPrefix + "/cash-counts";
        public const string Context = "context";
    }

    /// <summary>Vouchers (docx 6.1 to 6.6): entry per voucher type, View Voucher, Voucher Report.</summary>
    public static class Vouchers
    {
        public const string Root = VersionPrefix + "/vouchers";
        public const string PaymentEntryContext = "payment/entry-context";
        public const string PaymentTargets = "payment/targets";
        public const string Payment = "payment";
        public const string ReceiptEntryContext = "receipt/entry-context";
        public const string ReceiptTargets = "receipt/targets";
        public const string Receipt = "receipt";
        public const string ExpenseEntryContext = "expense/entry-context";
        public const string ExpenseTargets = "expense/targets";
        public const string Expense = "expense";
        public const string JournalEntryContext = "journal/entry-context";
        public const string JournalTargets = "journal/targets";
        public const string Journal = "journal";
        public const string ByNumber = "by-number";
        public const string Report = "report";
        public const string VoucherById = "{voucherId:guid}";
        public const string Description = "{voucherId:guid}/description";
        public const string Void = "{voucherId:guid}/void";
    }

    /// <summary>Account Ledger, Account Balance, Trial Balance, Detailed Trial Balance and General Journal (docx 8.1 to 8.4, 8.28).</summary>
    public static class AccountReports
    {
        public const string Root = VersionPrefix + "/account-reports";
        public const string Accounts = "accounts";
        public const string BalanceTypes = "balance-types";
        public const string Ledger = "ledger";
        public const string Balances = "balances";
        public const string TrialBalance = "trial-balance";
        public const string DetailTrialBalance = "detail-trial-balance";
        public const string GeneralJournal = "general-journal";
    }

    /// <summary>Closing and reopening months of the books (Part 15).</summary>
    public static class AccountingPeriods
    {
        public const string Root = VersionPrefix + "/accounting-periods";
        public const string Close = "close";
        public const string Reopen = "reopen";
    }

    /// <summary>Cash and bank account balances (cash management, Part 15).</summary>
    public static class CashAndBankBalances
    {
        public const string Root = VersionPrefix + "/cash-and-bank-balances";
    }

    /// <summary>Products, sizes, price check, ledger and the product report (docx chapter 4).</summary>
    public static class Products
    {
        public const string Root = VersionPrefix + "/products";
        public const string Variants = "variants";
        public const string VariantsSearch = "variants/search";
        public const string VariantsForDiscounts = "variants/for-discounts";
        public const string VariantsForDiscountRemoval = "variants/for-discount-removal";
        public const string LookupForLedger = "lookup/for-ledger";
        public const string LookupForDiscountsOnStores = "lookup/for-discounts-on-stores";
        public const string LookupForLabels = "lookup/for-labels";
        public const string ById = "{productId:guid}";
        public const string NextCode = "next-code";
        public const string PriceHistory = "{productId:guid}/price-history";
        public const string Image = "{productId:guid}/images/{slot:int}";
        public const string PriceCheck = "price-check";
        public const string Ledger = "ledger";
        public const string StockByStore = "stock-by-store";
        public const string Report = "report";
    }

    /// <summary>Bulk price changes (docx 4.7, 4.8, 4.10).</summary>
    public static class ProductPrices
    {
        public const string Root = VersionPrefix + "/product-prices";
        public const string OldToNewPreview = "old-to-new/preview";
        public const string OldToNew = "old-to-new";
        public const string PercentagePreview = "percentage/preview";
        public const string Percentage = "percentage";
        public const string NewSalePrice = "new-sale-price";
    }

    /// <summary>Product discounts as promotion rules (docx 4.9, 4.11, 4.12).</summary>
    public static class ProductDiscounts
    {
        public const string Root = VersionPrefix + "/product-discounts";
        public const string StockRangePreview = "stock-range/preview";
        public const string StockRange = "stock-range";
        public const string StoreRows = "stores";
        public const string OnStores = "on-stores";
        public const string Remove = "remove";
    }

    /// <summary>Change Product Attributes (docx 4.6).</summary>
    public static class ProductAttributes
    {
        public const string Root = VersionPrefix + "/product-attributes";
        public const string ProductsWithValue = "products";
        public const string Apply = "apply";
    }

    /// <summary>Replace Barcodes (docx 4.13), barcode history and the Generate Bar Codes labels.</summary>
    public static class Barcodes
    {
        public const string Root = VersionPrefix + "/barcodes";
        public const string ReplacementFile = "replacement-file";
        public const string ReplacementPreview = "replacement-preview";
        public const string Replace = "replace";
        public const string History = "history";
        public const string Labels = "labels";
    }

    public static class PriceBands
    {
        public const string Root = VersionPrefix + "/price-bands";
        public const string ById = "{priceBandId:guid}";
    }

    /// <summary>Staff profiles, salary terms and the company-wide list of staff transfers (docx 3.1 staff, Part 16.1). Staff transfers themselves are posted through <see cref="Parties"/>.</summary>
    public static class Staff
    {
        public const string Root = VersionPrefix + "/staff";
        public const string Transfers = "transfers";
        public const string StaffById = "{partyId:guid}";
        public const string SalaryTerms = "{partyId:guid}/salary-terms";
    }

    /// <summary>The monthly salary sheet, adjustments and salary payments (docx 3.8, 3.9; Part 16.3) and the Staff Salary / Salary Outstanding reports.</summary>
    public static class Payroll
    {
        public const string Root = VersionPrefix + "/payroll";
        public const string Sheet = "sheets";
        public const string PrepareSheet = "sheets/prepare";
        public const string ApproveSheet = "sheets/{runId:guid}/approve";
        public const string Adjustments = "adjustments";
        public const string LinePayments = "lines/{lineId:guid}/payments";
        public const string Payments = "payments";
        public const string StaffSalaryReport = "reports/staff-salary";
        public const string SalaryOutstandingReport = "reports/salary-outstanding";
    }

    /// <summary>New Sale, Sale Return, Exchange Invoice, View Invoices and View Deleted Invoices (docx 5.1 to 5.3, 5.13; Parts 11 and 12). Every screen has its own routes because the screens are granted separately.</summary>
    public static class Sales
    {
        public const string Root = VersionPrefix + "/sales";

        // shared tails, repeated under each screen prefix
        public const string EntryContextTail = "entry-context";
        public const string ItemByBarcodeTail = "items/by-barcode";
        public const string ItemSearchTail = "items/search";
        public const string ItemPictureTail = "items/{productId:guid}/picture";
        public const string CustomersTail = "customers";
        public const string SalesmenTail = "salesmen";
        public const string PaymentAccountsTail = "payment-accounts";
        public const string PosTerminalsTail = "pos-terminals";
        public const string InvoiceByNumberTail = "invoice-by-number";

        public const string NewSale = "new-sale";
        public const string NewSaleEntryContext = NewSale + "/" + EntryContextTail;
        public const string NewSaleItemByBarcode = NewSale + "/" + ItemByBarcodeTail;
        public const string NewSaleItemSearch = NewSale + "/" + ItemSearchTail;
        public const string NewSaleItemPicture = NewSale + "/" + ItemPictureTail;
        public const string NewSaleCustomers = NewSale + "/" + CustomersTail;
        public const string NewSaleSalesmen = NewSale + "/" + SalesmenTail;
        public const string NewSalePaymentAccounts = NewSale + "/" + PaymentAccountsTail;
        public const string NewSalePosTerminals = NewSale + "/" + PosTerminalsTail;

        public const string SaleReturns = "sale-returns";
        public const string SaleReturnEntryContext = SaleReturns + "/" + EntryContextTail;
        public const string SaleReturnInvoiceByNumber = SaleReturns + "/" + InvoiceByNumberTail;

        public const string Exchanges = "exchanges";
        public const string ExchangeEntryContext = Exchanges + "/" + EntryContextTail;
        public const string ExchangeInvoiceByNumber = Exchanges + "/" + InvoiceByNumberTail;
        public const string ExchangeItemByBarcode = Exchanges + "/" + ItemByBarcodeTail;
        public const string ExchangeItemSearch = Exchanges + "/" + ItemSearchTail;
        public const string ExchangeItemPicture = Exchanges + "/" + ItemPictureTail;
        public const string ExchangeCustomers = Exchanges + "/" + CustomersTail;
        public const string ExchangeSalesmen = Exchanges + "/" + SalesmenTail;
        public const string ExchangePaymentAccounts = Exchanges + "/" + PaymentAccountsTail;
        public const string ExchangePosTerminals = Exchanges + "/" + PosTerminalsTail;

        public const string Invoices = "invoices";
        public const string InvoiceById = Invoices + "/{invoiceId:guid}";
        public const string InvoicePrintDocument = InvoiceById + "/print-document";
        public const string InvoiceVoid = InvoiceById + "/void";
        public const string InvoiceVoidAndReenter = InvoiceById + "/void-and-reenter";
        public const string InvoiceCustomers = Invoices + "/" + CustomersTail;
        public const string InvoiceSalesmen = Invoices + "/" + SalesmenTail;

        public const string DeletedInvoices = "deleted-invoices";
        public const string DeletedInvoiceById = DeletedInvoices + "/{invoiceId:guid}";
        public const string DeletedInvoicePrintDocument = DeletedInvoiceById + "/print-document";
    }

    // <api-routes:end> (add one nested class per new controller above this line)
}
