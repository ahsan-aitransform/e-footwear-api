using EFootwear.DAL.Core.Storage;
using EFootwear.Domain.Common;
using EFootwear.Domain.Core;
using EFootwear.Domain.Core.Settings;
using EFootwear.Domain.Platform;

namespace EFootwear.Tests.Settings;

/// <summary>Fast rule tests that need no database: validation, merging, image detection, storage key safety.</summary>
public class SettingsDomainRulesTests
{
    private static BusinessInfoSettings BusinessInfo(string? name = "Shoe Zone", string? email = null, string? invoiceText = null) =>
        new(name, "  SZ  ", "", " 0300-1234567 ", null, email, "Main Road", null, invoiceText);

    [Fact]
    public void Business_info_is_trimmed_and_empty_text_becomes_not_set()
    {
        var normalized = BusinessInfoSettingsRules.NormalizeForCompany(BusinessInfo());

        Assert.Equal("Shoe Zone", normalized.BusinessName);
        Assert.Equal("SZ", normalized.BarcodeName);
        Assert.Null(normalized.BusinessNameUrdu);
        Assert.Equal("0300-1234567", normalized.PhoneNumber);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("a b@c.com")]
    public void An_invalid_email_is_refused(string email)
    {
        var exception = Assert.Throws<DomainException>(() => BusinessInfoSettingsRules.NormalizeForCompany(BusinessInfo(email: email)));
        Assert.Equal(ErrorKind.Validation, exception.Kind);
    }

    [Fact]
    public void The_company_business_name_is_required_but_a_store_cannot_override_it()
    {
        Assert.Throws<DomainException>(() => BusinessInfoSettingsRules.NormalizeForCompany(BusinessInfo(name: "  ")));
        Assert.Throws<DomainException>(() => BusinessInfoSettingsRules.NormalizeForStore(BusinessInfo(name: "Other")));
        Assert.Null(BusinessInfoSettingsRules.NormalizeForStore(BusinessInfo(name: null)).BusinessName);
    }

    [Fact]
    public void A_store_value_wins_over_the_company_value_and_an_empty_one_inherits()
    {
        var company = new BusinessInfoSettings("Co", "CO", null, "111", null, "co@x.com", "Company address", null, "Company terms");
        var store = new BusinessInfoSettings(null, null, null, "222", null, null, null, null, null);

        var effective = BusinessInfoSettingsRules.Merge(company, store);

        Assert.Equal("222", effective.PhoneNumber);
        Assert.Equal("co@x.com", effective.Email);
        Assert.Equal("Company terms", effective.InvoiceText);
        Assert.Equal("Co", effective.BusinessName);
    }

    [Fact]
    public void Printing_values_are_checked_against_the_allowed_lists_and_ranges()
    {
        Assert.Throws<DomainException>(() => PrintingSettingsRules.Normalize(new PrintingSettings("A3", null, null, null, null, null, null)));
        Assert.Throws<DomainException>(() => PrintingSettingsRules.Normalize(new PrintingSettings(null, "fr", null, null, null, null, null)));
        Assert.Throws<DomainException>(() => PrintingSettingsRules.Normalize(new PrintingSettings(null, null, null, null, 5, null, null)));
        Assert.Throws<DomainException>(() => PrintingSettingsRules.Normalize(new PrintingSettings(null, null, null, null, null, null, 0)));

        var valid = PrintingSettingsRules.Normalize(new PrintingSettings(PrintFormatCodes.Thermal, SupportedLanguageCodes.Urdu, true, true, 50, 30, 2));
        Assert.Equal("THERMAL", valid.DefaultPrintFormat);
    }

    [Fact]
    public void Tax_and_inventory_policy_keep_their_consistency_rules_and_invent_no_default()
    {
        Assert.Throws<DomainException>(() => TaxSettingsRules.Normalize(new TaxSettings(null, 17m)));
        Assert.Throws<DomainException>(() => TaxSettingsRules.Normalize(new TaxSettings(true, 100.5m)));
        Assert.Throws<DomainException>(() => TaxSettingsRules.Normalize(new TaxSettings(true, 1.23456m)));
        Assert.Equal(17.5m, TaxSettingsRules.Normalize(new TaxSettings(true, 17.5m)).DefaultTaxRatePercentage);

        Assert.Throws<DomainException>(() => InventoryPolicySettingsRules.Normalize(new InventoryPolicySettings(null, false, 5)));
        Assert.Throws<DomainException>(() => InventoryPolicySettingsRules.Normalize(new InventoryPolicySettings(null, true, -1)));
        var notConfigured = InventoryPolicySettingsRules.Normalize(new InventoryPolicySettings(null, null, null));
        Assert.Null(notConfigured.AllowNegativeStock);

        Assert.Throws<DomainException>(() => PromotionPolicySettingsRules.Normalize(new PromotionPolicySettings(100.1m)));
    }

    [Fact]
    public void Payment_methods_must_list_every_method_once_and_nothing_unknown()
    {
        var all = PaymentMethodCodes.All.Select((code, index) => new PaymentMethodSetting(code, true, null, null, index)).ToList();
        Assert.Equal(5, PaymentMethodsSettingsRules.Normalize(new PaymentMethodsSettings(all)).Methods.Count);

        Assert.Throws<DomainException>(() => PaymentMethodsSettingsRules.Normalize(new PaymentMethodsSettings(all.Skip(1).ToList())));
        Assert.Throws<DomainException>(() => PaymentMethodsSettingsRules.Normalize(new PaymentMethodsSettings([.. all, all[0]])));
        Assert.Throws<DomainException>(() => PaymentMethodsSettingsRules.Normalize(
            new PaymentMethodsSettings([.. all.Skip(1), new PaymentMethodSetting("BITCOIN", true, null, null, 9)])));
        Assert.All(PaymentMethodsSettingsRules.DefaultWhenNotConfigured().Methods, method => Assert.True(method.IsEnabled));
    }

    [Fact]
    public void The_image_type_is_decided_by_the_bytes_not_by_what_the_client_says()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0];
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0];
        byte[] webp = [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 1, 2, 3, 4, (byte)'W', (byte)'E', (byte)'B', (byte)'P'];
        byte[] script = "<script>alert(1)</script>"u8.ToArray();
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'></svg>"u8.ToArray();

        Assert.Equal(AttachmentRules.PngContentType, AttachmentRules.DetectImageContentType(png));
        Assert.Equal(AttachmentRules.JpegContentType, AttachmentRules.DetectImageContentType(jpeg));
        Assert.Equal(AttachmentRules.WebpContentType, AttachmentRules.DetectImageContentType(webp));
        Assert.Null(AttachmentRules.DetectImageContentType(script));
        Assert.Null(AttachmentRules.DetectImageContentType(svg));
        Assert.Null(AttachmentRules.DetectImageContentType([]));
    }

    [Fact]
    public void A_document_series_only_moves_forward()
    {
        DocumentNumberingRules.EnsureNextValueDoesNotGoBack(500, 500);
        DocumentNumberingRules.EnsureNextValueDoesNotGoBack(501, 500);
        DocumentNumberingRules.EnsureNextValueDoesNotGoBack(1, null);

        var back = Assert.Throws<DomainException>(() => DocumentNumberingRules.EnsureNextValueDoesNotGoBack(499, 500));
        Assert.Equal(ErrorCodes.DocumentNumberWouldRepeat, back.ErrorCode);
        Assert.Throws<DomainException>(() => DocumentNumberingRules.EnsureNextValueDoesNotGoBack(0, null));
        Assert.Throws<DomainException>(() => DocumentNumberingRules.EnsureValidSeriesCode("bad code"));
        Assert.Equal("SALE_INVOICE", DocumentNumberingRules.EnsureValidSeriesCode(" SALE_INVOICE "));
    }

    [Fact]
    public void Every_known_series_code_is_a_valid_unique_code()
    {
        Assert.Equal(DocumentNumberSeriesCodes.All.Count, DocumentNumberSeriesCodes.All.Distinct().Count());
        Assert.All(DocumentNumberSeriesCodes.All, code => DocumentNumberingRules.EnsureValidSeriesCode(code));
    }

    [Fact]
    public void Company_profile_and_plan_rules_normalize_and_refuse()
    {
        Assert.Equal("PKR", CompanyProfileRules.NormalizeCurrencyCode(" pkr "));
        Assert.Throws<DomainException>(() => CompanyProfileRules.NormalizeCurrencyCode("RUPEE"));
        Assert.Throws<DomainException>(() => CompanyProfileRules.NormalizeRoundingMode("UP"));
        Assert.Throws<DomainException>(() => CompanyProfileRules.NormalizePriceBandBasis(null));
        Assert.Throws<DomainException>(() => CompanyProfileRules.NormalizeTimezone(" "));

        Assert.Equal("GOLD_2", PlanRules.EnsureValidCode(" gold_2 "));
        Assert.Throws<DomainException>(() => PlanRules.EnsureValidCode("1GOLD"));
        Assert.Throws<DomainException>(() => PlanRules.EnsureValidLimits(0, 10));
        Assert.Throws<DomainException>(() => PlanRules.EnsureValidLimits(10, PlanRules.LimitMaximum + 1));
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("companies/../../secret.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("companies\\x\\logo.png")]
    [InlineData("companies//logo.png")]
    [InlineData("")]
    public async Task The_local_file_storage_refuses_a_key_that_could_leave_its_folder(string storageKey)
    {
        var root = Path.Combine(Path.GetTempPath(), "efootwear-unit-storage", Guid.NewGuid().ToString("N"));
        var storage = new LocalFolderFileStorage(root);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.SaveAsync(storageKey, new MemoryStream([1, 2, 3]), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenReadAsync(storageKey, CancellationToken.None));
    }

    [Fact]
    public async Task The_local_file_storage_saves_reads_and_never_overwrites()
    {
        var root = Path.Combine(Path.GetTempPath(), "efootwear-unit-storage", Guid.NewGuid().ToString("N"));
        var storage = new LocalFolderFileStorage(root);
        const string key = "companies/abc/logo/one.png";

        await storage.SaveAsync(key, new MemoryStream([9, 8, 7]), CancellationToken.None);
        await using (var read = await storage.OpenReadAsync(key, CancellationToken.None))
        {
            Assert.NotNull(read);
            using var copy = new MemoryStream();
            await read!.CopyToAsync(copy);
            Assert.Equal(new byte[] { 9, 8, 7 }, copy.ToArray());
        }

        await Assert.ThrowsAsync<IOException>(() => storage.SaveAsync(key, new MemoryStream([1]), CancellationToken.None));
        Assert.Null(await storage.OpenReadAsync("companies/abc/logo/missing.png", CancellationToken.None));
    }
}
