using System.Reflection;
using System.Text.RegularExpressions;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Security;

/// <summary>The permission catalog (single source of truth) and the constants generated from it. No database needed.</summary>
public partial class PermissionCatalogTests
{
    private const int LegacyPermissionCount = 38;

    [GeneratedRegex(@"^[A-Z0-9_]+\.[A-Z0-9_]+\.[A-Z_]+$")]
    private static partial Regex DottedCodePattern();

    [GeneratedRegex(@"^[A-Z_]+$")]
    private static partial Regex LegacyCodePattern();

    [Fact]
    public void Generated_permission_constants_are_up_to_date_with_the_catalog()
    {
        var expected = PermissionCodesSourceGenerator.GenerateSource(PermissionCatalog.Modules);
        var actual = File.ReadAllText(RepositoryPaths.GeneratedPermissionCodesFile).ReplaceLineEndings("\n");

        Assert.True(expected == actual,
            "PermissionCodes.generated.cs is stale. Run: dotnet run --project E-Footwear-Api -- generate-permission-codes");
    }

    [Fact]
    public void Every_constant_is_a_catalog_code_and_every_catalog_code_has_a_constant()
    {
        var constantValues = new List<string>();
        void Collect(Type type)
        {
            constantValues.AddRange(type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(field => field.IsLiteral).Select(field => (string)field.GetRawConstantValue()!));
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public)) Collect(nested);
        }
        Collect(typeof(PermissionCodes));

        Assert.Equal(PermissionCatalog.AllPermissionCodes.Count, constantValues.Count);
        Assert.True(PermissionCatalog.AllPermissionCodes.SetEquals(constantValues));
    }

    [Fact]
    public void Codes_follow_the_module_screen_action_format()
    {
        foreach (var entry in PermissionCatalog.AllEntries)
        {
            var code = entry.Action.PermissionCode;
            if (entry.ModuleCode == PermissionCatalog.RestrictedOperationsModuleCode)
            {
                Assert.Matches(LegacyCodePattern(), code);
                Assert.Equal(code, entry.Action.ActionCode);
            }
            else
            {
                Assert.Matches(DottedCodePattern(), code);
                Assert.Equal($"{entry.ModuleCode}.{entry.ScreenCode}.{entry.Action.ActionCode}", code);
            }
        }
    }

    [Fact]
    public void All_legacy_codes_of_the_schema_are_in_the_catalog()
    {
        var legacy = PermissionCatalog.AllEntries.Where(entry => entry.ModuleCode == PermissionCatalog.RestrictedOperationsModuleCode).ToList();
        Assert.Equal(LegacyPermissionCount, legacy.Count);
        string[] mustExist = ["SALE_CREATE", "PRICE_CHANGE", "DISCOUNT_MANUAL", "VOUCHER_POST", "TRANSFER_RECEIVE", "REPORT_VIEW",
                              "USER_PASSWORD_RESET", "USER_BLOCK", "LOGIN_HISTORY_VIEW", "SYNC_CONFLICT_RESOLVE", "STOCK_EXCEPTION_RESOLVE"];
        foreach (var code in mustExist) Assert.Contains(code, PermissionCatalog.AllPermissionCodes);
    }

    [Fact]
    public void The_original_menu_tree_is_present_with_one_name_per_screen()
    {
        var moduleCodes = PermissionCatalog.Modules.Select(module => module.ModuleCode).ToList();
        foreach (var moduleCode in new[] { "USER_ACCOUNT", "PRODUCTS", "PARTY", "INVOICE", "ORDERS", "VOUCHER", "REPORTS", "SETTINGS" })
            Assert.Contains(moduleCode, moduleCodes);

        var reports = PermissionCatalog.Modules.Single(module => module.ModuleCode == "REPORTS");
        Assert.Equal(["ACCOUNT_LEDGER", "ACCOUNT_BALANCE", "TRIAL_BALANCE", "DETAIL_TRIAL_BALANCE"],
            reports.Screens.Where(screen => screen.SubGroupCode == "ACCOUNT_REPORTS").Select(screen => screen.ScreenCode));

        var settings = PermissionCatalog.Modules.Single(module => module.ModuleCode == "SETTINGS");
        Assert.Equal(["GL_ACCOUNTS", "NEW_SECOND_LEVEL", "VIEW_SECOND_LEVEL"],
            settings.Screens.Where(screen => screen.SubGroupCode == "GL_ACCOUNT").Select(screen => screen.ScreenCode));

        var invoice = PermissionCatalog.Modules.Single(module => module.ModuleCode == "INVOICE");
        var newSale = invoice.Screens.Single(screen => screen.ScreenCode == "NEW_SALE");
        Assert.Contains(newSale.Actions, action => action.ActionCode == PermissionActionCodes.ChangeSellingPrice);
        Assert.Contains(newSale.Actions, action => action.ActionCode == PermissionActionCodes.GiveDiscount);

        // names the docx lists twice are unified: only the menu name exists
        Assert.DoesNotContain(invoice.Screens, screen => screen.ScreenCode is "SALE_INVOICE" or "PURCHASE_INVOICE");
        Assert.Contains("REPLACE_BARCODES", PermissionCatalog.Modules.Single(module => module.ModuleCode == "PRODUCTS").Screens.Select(screen => screen.ScreenCode));
    }

    [Fact]
    public void Role_templates_only_name_catalog_codes_and_the_owner_template_is_everything()
    {
        foreach (var roleCode in new[] { RoleCodes.CompanyOwner, RoleCodes.StoreManager, RoleCodes.CashierSalesman, RoleCodes.Accountant })
            foreach (var code in DefaultRolePermissionTemplates.PermissionCodesForRole(roleCode))
                Assert.Contains(code, PermissionCatalog.AllPermissionCodes);

        Assert.Equal(PermissionCatalog.AllPermissionCodes.Count, DefaultRolePermissionTemplates.PermissionCodesForRole(RoleCodes.CompanyOwner).Count);
        Assert.False(DefaultRolePermissionTemplates.IsCopiedToUserPermissions(RoleCodes.CompanyOwner));
        Assert.Empty(DefaultRolePermissionTemplates.PermissionCodesForRole("NO_SUCH_ROLE"));
        Assert.Contains(PermissionCodes.UserAccount.CreateUser.Add, DefaultRolePermissionTemplates.PermissionCodesForRole(RoleCodes.StoreManager));
        Assert.DoesNotContain(PermissionCodes.UserAccount.CreateUser.Add, DefaultRolePermissionTemplates.PermissionCodesForRole(RoleCodes.CashierSalesman));
    }

    [Fact]
    public void Pascal_case_conversion_is_predictable()
    {
        Assert.Equal("StockAdjustmentNonZeroStock", PermissionCodesSourceGenerator.ToPascalCase("STOCK_ADJUSTMENT_NON_ZERO_STOCK"));
        Assert.Equal("ChangeSellingPrice", PermissionCodesSourceGenerator.ToPascalCase("CHANGE_SELLING_PRICE"));
    }
}
