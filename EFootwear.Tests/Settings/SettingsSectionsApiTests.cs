using System.Net;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Settings;

/// <summary>Business Info, regional preferences, printing, tax, inventory and promotion policy, payment methods (docx 2.19, Part 7).</summary>
[Collection(ApiIntegrationCollection.Name)]
public class SettingsSectionsApiTests(ApiTestEnvironment environment)
{
    private static readonly string BusinessInfoUrl = SettingsUrls.Settings(ApiRoutes.CompanySettings.BusinessInfo);
    private static readonly string InventoryPolicyUrl = SettingsUrls.Settings(ApiRoutes.CompanySettings.InventoryPolicy);
    private static readonly string TaxUrl = SettingsUrls.Settings(ApiRoutes.CompanySettings.Tax);
    private static readonly string PromotionPolicyUrl = SettingsUrls.Settings(ApiRoutes.CompanySettings.PromotionPolicy);
    private static readonly string RegionalUrl = SettingsUrls.Settings(ApiRoutes.CompanySettings.RegionalPreferences);
    private static readonly string PrintingUrl = SettingsUrls.Settings(ApiRoutes.CompanySettings.Printing);
    private static readonly string PaymentMethodsUrl = SettingsUrls.Settings(ApiRoutes.CompanySettings.PaymentMethods);
    private static readonly string EnabledPaymentMethodsUrl = SettingsUrls.Settings(ApiRoutes.CompanySettings.EnabledPaymentMethods);

    private static object BusinessInfo(string businessName = "Bata Pakistan", string? email = "info@bata.example", string? mobile = null) => new
    {
        businessName,
        barcodeName = "BATA",
        businessNameUrdu = "باٹا پاکستان",
        phoneNumber = "042-111",
        mobileNumber = mobile,
        email,
        addressEnglish = "Mall Road",
        addressUrdu = "مال روڈ",
        invoiceText = "Exchange as per the shop policy",
    };

    [Fact]
    public async Task Nothing_is_configured_at_first_and_no_policy_is_invented()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);

        foreach (var url in new[] { InventoryPolicyUrl, TaxUrl, PromotionPolicyUrl, RegionalUrl, PrintingUrl })
        {
            var document = await (await scenario.OwnerClient.GetAsync(url)).ReadSuccessJsonAsync();
            Assert.False(document.GetProperty("isStored").GetBoolean(), url);
            Assert.Equal(0, document.GetProperty("rowVersion").GetInt64());
            Assert.All(document.GetProperty("values").EnumerateObject(), property => Assert.Equal(JsonValueKind.Null, property.Value.ValueKind));
        }
    }

    [Fact]
    public async Task Business_info_saves_renames_the_company_and_refuses_a_stale_version()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);

        var initial = await (await scenario.OwnerClient.GetAsync(BusinessInfoUrl)).ReadSuccessJsonAsync();
        Assert.False(initial.GetProperty("isStored").GetBoolean());
        Assert.Contains("Test Company", initial.GetProperty("values").GetProperty("businessName").GetString());

        var saved = await (await scenario.OwnerClient.PutSettingsAsync(BusinessInfoUrl, BusinessInfo("  Bata Pakistan  "))).ReadSuccessJsonAsync();
        Assert.True(saved.GetProperty("isStored").GetBoolean());
        var values = saved.GetProperty("values");
        Assert.Equal("Bata Pakistan", values.GetProperty("businessName").GetString());
        Assert.Equal("باٹا پاکستان", values.GetProperty("businessNameUrdu").GetString());
        Assert.Equal("Exchange as per the shop policy", values.GetProperty("invoiceText").GetString());
        var version = saved.GetProperty("rowVersion").GetInt64();

        var profile = await (await scenario.OwnerClient.GetAsync(SettingsUrls.CompanyProfile)).ReadSuccessJsonAsync();
        Assert.Equal("Bata Pakistan", profile.GetProperty("name").GetString());
        Assert.Equal("Exchange as per the shop policy", profile.GetProperty("invoiceText").GetString());

        var second = await (await scenario.OwnerClient.PutSettingsAsync(BusinessInfoUrl, BusinessInfo(), version)).ReadSuccessJsonAsync();
        Assert.True(second.GetProperty("rowVersion").GetInt64() > version);

        var stale = await scenario.OwnerClient.PutSettingsAsync(BusinessInfoUrl, BusinessInfo(), version);
        await stale.AssertProblemAsync(HttpStatusCode.Conflict, "CONCURRENT_UPDATE");
    }

    [Fact]
    public async Task Invalid_business_info_is_refused_with_a_message()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);

        await (await scenario.OwnerClient.PutSettingsAsync(BusinessInfoUrl, BusinessInfo(email: "not-an-email")))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await (await scenario.OwnerClient.PutSettingsAsync(BusinessInfoUrl, BusinessInfo(businessName: "   ")))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await (await scenario.OwnerClient.PutSettingsAsync(BusinessInfoUrl, BusinessInfo(mobile: new string('9', 31))))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Policy_sections_save_read_back_and_keep_their_consistency_rules()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);

        var inventory = await (await scenario.OwnerClient.PutSettingsAsync(InventoryPolicyUrl,
            new { allowNegativeStock = false, allowBackdatedStockAdjustments = true, backdatedStockAdjustmentMaximumDays = 7 })).ReadSuccessJsonAsync();
        Assert.False(inventory.GetProperty("values").GetProperty("allowNegativeStock").GetBoolean());
        Assert.Equal(7, inventory.GetProperty("values").GetProperty("backdatedStockAdjustmentMaximumDays").GetInt32());
        await (await scenario.OwnerClient.PutSettingsAsync(InventoryPolicyUrl,
            new { allowNegativeStock = (bool?)null, allowBackdatedStockAdjustments = false, backdatedStockAdjustmentMaximumDays = 3 }))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");

        var tax = await (await scenario.OwnerClient.PutSettingsAsync(TaxUrl, new { isTaxEnabled = true, defaultTaxRatePercentage = 17.5 })).ReadSuccessJsonAsync();
        Assert.Equal(17.5, tax.GetProperty("values").GetProperty("defaultTaxRatePercentage").GetDouble());
        await (await scenario.OwnerClient.PutSettingsAsync(TaxUrl, new { isTaxEnabled = (bool?)null, defaultTaxRatePercentage = 5 }))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");

        var promotion = await (await scenario.OwnerClient.PutSettingsAsync(PromotionPolicyUrl, new { maximumManualDiscountPercentage = 30 })).ReadSuccessJsonAsync();
        Assert.Equal(30, promotion.GetProperty("values").GetProperty("maximumManualDiscountPercentage").GetDouble());
        await (await scenario.OwnerClient.PutSettingsAsync(PromotionPolicyUrl, new { maximumManualDiscountPercentage = 120 }))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");

        var regional = await (await scenario.OwnerClient.PutSettingsAsync(RegionalUrl,
            new { dateFormat = "DD/MM/YYYY", timeFormat = "24H", defaultLanguageCode = "ur" })).ReadSuccessJsonAsync();
        Assert.Equal("ur", regional.GetProperty("values").GetProperty("defaultLanguageCode").GetString());
        await (await scenario.OwnerClient.PutSettingsAsync(RegionalUrl, new { dateFormat = "YY-M-D", timeFormat = (string?)null, defaultLanguageCode = (string?)null }))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");

        await (await scenario.OwnerClient.PutSettingsAsync(PrintingUrl,
            new { defaultPrintFormat = "A3", printLanguageCode = (string?)null, printRightToLeft = (bool?)null, printLogoOnDocuments = (bool?)null,
                  barcodeLabelWidthMillimetres = (int?)null, barcodeLabelHeightMillimetres = (int?)null, receiptCopies = (int?)null }))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task A_store_overrides_the_company_values_and_inherits_the_rest()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        var storeId = scenario.Company.FirstStoreId;
        var storePrintingUrl = SettingsUrls.StoreSection(ApiRoutes.CompanySettings.StorePrinting, storeId);
        var storeBusinessUrl = SettingsUrls.StoreSection(ApiRoutes.CompanySettings.StoreBusinessInfo, storeId);

        await (await scenario.OwnerClient.PutSettingsAsync(PrintingUrl, new
        {
            defaultPrintFormat = "A4", printLanguageCode = (string?)null, printRightToLeft = (bool?)null, printLogoOnDocuments = true,
            barcodeLabelWidthMillimetres = (int?)null, barcodeLabelHeightMillimetres = (int?)null, receiptCopies = 3,
        })).ReadSuccessJsonAsync();

        var saved = await (await scenario.OwnerClient.PutSettingsAsync(storePrintingUrl, new
        {
            defaultPrintFormat = (string?)null, printLanguageCode = (string?)null, printRightToLeft = (bool?)null, printLogoOnDocuments = (bool?)null,
            barcodeLabelWidthMillimetres = (int?)null, barcodeLabelHeightMillimetres = (int?)null, receiptCopies = 2,
        })).ReadSuccessJsonAsync();
        Assert.Equal(2, saved.GetProperty("overrides").GetProperty("receiptCopies").GetInt32());
        Assert.Equal(2, saved.GetProperty("effective").GetProperty("receiptCopies").GetInt32());
        Assert.Equal("A4", saved.GetProperty("effective").GetProperty("defaultPrintFormat").GetString());
        Assert.True(saved.GetProperty("effective").GetProperty("printLogoOnDocuments").GetBoolean());

        // clearing the override makes the store inherit again
        var cleared = await (await scenario.OwnerClient.PutSettingsAsync(storePrintingUrl, new
        {
            defaultPrintFormat = (string?)null, printLanguageCode = (string?)null, printRightToLeft = (bool?)null, printLogoOnDocuments = (bool?)null,
            barcodeLabelWidthMillimetres = (int?)null, barcodeLabelHeightMillimetres = (int?)null, receiptCopies = (int?)null,
        })).ReadSuccessJsonAsync();
        Assert.Equal(3, cleared.GetProperty("effective").GetProperty("receiptCopies").GetInt32());

        await (await scenario.OwnerClient.PutSettingsAsync(BusinessInfoUrl, BusinessInfo())).ReadSuccessJsonAsync();
        var businessOverride = await (await scenario.OwnerClient.PutSettingsAsync(storeBusinessUrl, new
        {
            businessName = (string?)null, barcodeName = (string?)null, businessNameUrdu = (string?)null, phoneNumber = "0300-0000000",
            mobileNumber = (string?)null, email = (string?)null, addressEnglish = (string?)null, addressUrdu = (string?)null, invoiceText = (string?)null,
        })).ReadSuccessJsonAsync();
        Assert.Equal("0300-0000000", businessOverride.GetProperty("effective").GetProperty("phoneNumber").GetString());
        Assert.Equal("Mall Road", businessOverride.GetProperty("effective").GetProperty("addressEnglish").GetString());

        await (await scenario.OwnerClient.PutSettingsAsync(storeBusinessUrl, new
        {
            businessName = "Store level name", barcodeName = (string?)null, businessNameUrdu = (string?)null, phoneNumber = (string?)null,
            mobileNumber = (string?)null, email = (string?)null, addressEnglish = (string?)null, addressUrdu = (string?)null, invoiceText = (string?)null,
        })).AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task A_store_manager_reaches_the_overrides_of_their_own_store_only()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        var manager = await scenario.CreateUserClientAsync("STORE_MANAGER", [secondStoreId],
            PermissionCodes.Settings.Preference.View, PermissionCodes.Settings.Preference.Edit);

        var own = SettingsUrls.StoreSection(ApiRoutes.CompanySettings.StorePrinting, secondStoreId);
        var foreign = SettingsUrls.StoreSection(ApiRoutes.CompanySettings.StorePrinting, scenario.Company.FirstStoreId);

        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync(own)).StatusCode);
        await (await manager.GetAsync(foreign)).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await manager.PutSettingsAsync(foreign, new { receiptCopies = 1 })).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync(BusinessInfoUrl)).StatusCode);   // company-wide values are readable
    }

    [Fact]
    public async Task Payment_methods_are_switched_on_or_off_and_every_user_sees_the_enabled_ones()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        var cashier = await scenario.CreateUserClientAsync("CASHIER_SALESMAN", [scenario.Company.FirstStoreId]);

        var initial = await (await scenario.OwnerClient.GetAsync(PaymentMethodsUrl)).ReadSuccessJsonAsync();
        Assert.False(initial.GetProperty("isStored").GetBoolean());
        Assert.Equal(5, initial.GetProperty("values").GetProperty("methods").GetArrayLength());
        Assert.Equal(5, (await (await cashier.GetAsync(EnabledPaymentMethodsUrl)).ReadSuccessJsonAsync()).GetArrayLength());

        object Method(string code, bool enabled, int order, string? name = null) =>
            new { code, isEnabled = enabled, displayNameEnglish = name, displayNameUrdu = (string?)null, sortOrder = order };
        var methods = new[]
        {
            Method("CASH", true, 0, "Cash"), Method("CARD", false, 1), Method("BANK_TRANSFER", true, 2),
            Method("CHEQUE", true, 3), Method("ONLINE", true, 4),
        };
        await (await scenario.OwnerClient.PutSettingsAsync(PaymentMethodsUrl, new { methods })).ReadSuccessJsonAsync();

        var enabled = await (await cashier.GetAsync(EnabledPaymentMethodsUrl)).ReadSuccessJsonAsync();
        var codes = enabled.EnumerateArray().Select(method => method.GetProperty("code").GetString()).ToList();
        Assert.Equal(["CASH", "BANK_TRANSFER", "CHEQUE", "ONLINE"], codes);

        await (await scenario.OwnerClient.PutSettingsAsync(PaymentMethodsUrl, new { methods = methods.Take(4) }))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await (await scenario.OwnerClient.PutSettingsAsync(PaymentMethodsUrl, new { methods = methods.Take(4).Append(Method("BITCOIN", true, 9)) }))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Every_settings_screen_needs_its_permission_on_the_server()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        var viewer = await scenario.CreateUserClientAsync("STORE_MANAGER", [scenario.Company.FirstStoreId], PermissionCodes.Settings.Preference.View);
        var nobody = await scenario.CreateUserClientAsync("CASHIER_SALESMAN", [scenario.Company.FirstStoreId]);

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(BusinessInfoUrl)).StatusCode);
        await (await viewer.PutSettingsAsync(BusinessInfoUrl, BusinessInfo())).AssertProblemAsync(HttpStatusCode.Forbidden, "PERMISSION_DENIED");
        await (await viewer.GetAsync(PaymentMethodsUrl)).AssertProblemAsync(HttpStatusCode.Forbidden, "PERMISSION_DENIED");
        await (await viewer.GetAsync(SettingsUrls.CompanyProfile)).AssertProblemAsync(HttpStatusCode.Forbidden, "PERMISSION_DENIED");

        foreach (var url in new[] { BusinessInfoUrl, InventoryPolicyUrl, TaxUrl, PromotionPolicyUrl, RegionalUrl, PrintingUrl, PaymentMethodsUrl })
            await (await nobody.GetAsync(url)).AssertProblemAsync(HttpStatusCode.Forbidden, "PERMISSION_DENIED");
        Assert.Equal(HttpStatusCode.OK, (await nobody.GetAsync(EnabledPaymentMethodsUrl)).StatusCode);

        await (await scenario.PlatformClient.GetAsync(BusinessInfoUrl)).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        var anonymous = environment.Scenarios.CreateAnonymousClient();
        await (await anonymous.GetAsync(BusinessInfoUrl)).AssertProblemAsync(HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    [Fact]
    public async Task Settings_never_cross_from_one_company_to_another()
    {
        var first = await SettingsScenario.CreateAsync(environment);
        var second = await SettingsScenario.CreateAsync(environment);
        await (await first.OwnerClient.PutSettingsAsync(TaxUrl, new { isTaxEnabled = true, defaultTaxRatePercentage = 12 })).ReadSuccessJsonAsync();
        await (await first.OwnerClient.PutSettingsAsync(BusinessInfoUrl, BusinessInfo())).ReadSuccessJsonAsync();

        var secondTax = await (await second.OwnerClient.GetAsync(TaxUrl)).ReadSuccessJsonAsync();
        Assert.False(secondTax.GetProperty("isStored").GetBoolean());
        var secondBusiness = await (await second.OwnerClient.GetAsync(BusinessInfoUrl)).ReadSuccessJsonAsync();
        Assert.Equal(JsonValueKind.Null, secondBusiness.GetProperty("values").GetProperty("barcodeName").ValueKind);

        var foreignStoreUrl = SettingsUrls.StoreSection(ApiRoutes.CompanySettings.StorePrinting, first.Company.FirstStoreId);
        await (await second.OwnerClient.GetAsync(foreignStoreUrl)).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        await (await second.OwnerClient.PutSettingsAsync(foreignStoreUrl, new { receiptCopies = 1 })).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }
}
