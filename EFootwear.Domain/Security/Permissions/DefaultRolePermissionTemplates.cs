namespace EFootwear.Domain.Security.Permissions;

/// <summary>
/// The permissions a new user starts with, by role.
///
/// SUGGESTION ONLY (rule A2.10): which role may do what is a business decision that has not been taken yet
/// ([BUSINESS DECISION REQUIRED]). These sets exist so that a freshly created user can work at all. The company owner
/// can change any user's permissions one by one afterwards; a template never prevents an individual override.
///
/// How it is used: when a user is created, the codes returned here are copied into sec.user_permissions.
/// The company owner is not copied: sec.require_permission (SQL) and RequestContext treat COMPANY_OWNER as holding
/// every permission of the catalog, so new screens reach the owner without any data change.
/// </summary>
public static class DefaultRolePermissionTemplates
{
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<string>>> TemplatesByRole = new(BuildTemplates);

    /// <summary>The suggested permission codes for a role. Unknown roles get an empty list.</summary>
    public static IReadOnlyList<string> PermissionCodesForRole(string roleCode) =>
        TemplatesByRole.Value.TryGetValue(roleCode, out var codes) ? codes : [];

    /// <summary>True when the role's permissions are copied into sec.user_permissions on creation.</summary>
    public static bool IsCopiedToUserPermissions(string roleCode) => roleCode != RoleCodes.CompanyOwner;

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildTemplates() =>
        new Dictionary<string, IReadOnlyList<string>>
        {
            [RoleCodes.CompanyOwner] = PermissionCatalog.AllPermissionCodes.OrderBy(code => code, StringComparer.Ordinal).ToList(),
            [RoleCodes.StoreManager] = BuildStoreManagerTemplate(),
            [RoleCodes.CashierSalesman] = BuildCashierSalesmanTemplate(),
            [RoleCodes.Accountant] = BuildAccountantTemplate(),
        };

    // Store manager: runs one store. Everything operational, except delete, plus the restricted price operations.
    private static IReadOnlyList<string> BuildStoreManagerTemplate()
    {
        var codes = new List<string>();
        string[] operationalModules = ["PRODUCTS", "PARTY", "INVOICE", "ORDERS", "VOUCHER", "REPORTS", "INVENTORY", "VENDORS", "EXPENSES", "DASHBOARDS"];
        foreach (var moduleCode in operationalModules)
            codes.AddRange(CodesOf(moduleCode, screenCode: null, [PermissionActionCodes.Delete], invertActions: true));

        codes.AddRange(CodesOf("USER_ACCOUNT", "CREATE_USER", [PermissionActionCodes.View, PermissionActionCodes.Add]));
        codes.AddRange(CodesOf("USER_ACCOUNT", "VIEW_USERS", [PermissionActionCodes.View]));
        codes.AddRange(CodesOf("USER_ACCOUNT", "USERS_ACTIVITY", [PermissionActionCodes.View]));
        codes.AddRange(CodesOf("USER_ACCOUNT", "CHANGE_PASSWORD"));

        codes.AddRange(
        [
            "SALE_CREATE", "SALE_RETURN", "PRICE_CHANGE", "DISCOUNT_MANUAL", "DISCOUNT_APPROVE", "PURCHASE_CREATE",
            "STOCK_ADJUST", "STOCK_AUDIT", "TRANSFER_CREATE", "TRANSFER_RECEIVE", "CLAIM_REQUEST", "EXPENSE_RECORD",
            "CASH_COUNT", "COMMISSION_VIEW", "REPORT_VIEW",
        ]);
        return Distinct(codes);
    }

    // Cashier / salesman: sells at the counter and looks up products and customers.
    private static IReadOnlyList<string> BuildCashierSalesmanTemplate()
    {
        var codes = new List<string>();
        codes.AddRange(CodesOf("INVOICE", "NEW_SALE", [PermissionActionCodes.View, PermissionActionCodes.Add, PermissionActionCodes.Print]));
        codes.AddRange(CodesOf("INVOICE", "VIEW_INVOICES", [PermissionActionCodes.View, PermissionActionCodes.Print]));
        codes.AddRange(CodesOf("PRODUCTS", "SEARCH_PRODUCT"));
        codes.AddRange(CodesOf("PRODUCTS", "PRICE_CHECK"));
        codes.AddRange(CodesOf("PRODUCTS", "VIEW_PRODUCTS", [PermissionActionCodes.View]));
        codes.AddRange(CodesOf("PARTY", "SEARCH_PARTY"));
        codes.AddRange(CodesOf("PARTY", "VIEW_PARTY", [PermissionActionCodes.View]));
        codes.AddRange(CodesOf("PARTY", "NEW_PARTY", [PermissionActionCodes.View, PermissionActionCodes.Add]));
        codes.AddRange(CodesOf("USER_ACCOUNT", "CHANGE_PASSWORD"));
        codes.Add("SALE_CREATE");
        return Distinct(codes);
    }

    // Accountant: vouchers, accounting reports, ledgers, expenses; reads parties, invoices and payroll.
    private static IReadOnlyList<string> BuildAccountantTemplate()
    {
        var codes = new List<string>();
        codes.AddRange(CodesOf("VOUCHER", screenCode: null, [PermissionActionCodes.Delete, PermissionActionCodes.Approve], invertActions: true));
        codes.AddRange(CodesOf("REPORTS"));
        codes.AddRange(CodesOf("EXPENSES", screenCode: null, [PermissionActionCodes.Delete], invertActions: true));
        codes.AddRange(CodesOf("PARTY", "VIEW_PARTY", [PermissionActionCodes.View, PermissionActionCodes.Print, PermissionActionCodes.Export]));
        codes.AddRange(CodesOf("PARTY", "SEARCH_PARTY"));
        codes.AddRange(CodesOf("PARTY", "ACCOUNT_DETAIL", [PermissionActionCodes.View, PermissionActionCodes.Print, PermissionActionCodes.Export]));
        codes.AddRange(CodesOf("INVOICE", "VIEW_INVOICES", [PermissionActionCodes.View, PermissionActionCodes.Print, PermissionActionCodes.Export]));
        codes.AddRange(CodesOf("HUMAN_RESOURCES", "SALARY_PAYMENTS", [PermissionActionCodes.View, PermissionActionCodes.Print, PermissionActionCodes.Export]));
        foreach (var glScreenCode in new[] { "GL_ACCOUNTS", "NEW_SECOND_LEVEL", "VIEW_SECOND_LEVEL", "OPENING_BALANCE" })
            codes.AddRange(CodesOf("SETTINGS", glScreenCode, [PermissionActionCodes.View, PermissionActionCodes.Add, PermissionActionCodes.Edit, PermissionActionCodes.Print, PermissionActionCodes.Export]));
        codes.AddRange(CodesOf("USER_ACCOUNT", "CHANGE_PASSWORD"));
        codes.AddRange(["VOUCHER_POST", "EXPENSE_RECORD", "REPORT_VIEW", "REPORT_ACCOUNTS", "PURCHASE_PAY", "OPENING_BALANCE", "CASH_COUNT", "COMMISSION_VIEW"]);
        return Distinct(codes);
    }

    /// <summary>
    /// Permission codes of one module (optionally one screen) filtered by action. With
    /// <paramref name="invertActions"/> the listed actions are the ones to leave out.
    /// </summary>
    private static IEnumerable<string> CodesOf(string moduleCode, string? screenCode = null,
                                               IReadOnlyCollection<string>? actionCodes = null, bool invertActions = false) =>
        PermissionCatalog.AllEntries
            .Where(entry => entry.ModuleCode == moduleCode)
            .Where(entry => screenCode is null || entry.ScreenCode == screenCode)
            .Where(entry => actionCodes is null || actionCodes.Contains(entry.Action.ActionCode) != invertActions)
            .Select(entry => entry.Action.PermissionCode);

    private static IReadOnlyList<string> Distinct(IEnumerable<string> codes)
    {
        var unknown = codes.Where(code => !PermissionCatalog.IsKnownPermissionCode(code)).Distinct().ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException($"A role template names permission codes that are not in the catalog: {string.Join(", ", unknown)}.");
        return codes.Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToList();
    }
}
