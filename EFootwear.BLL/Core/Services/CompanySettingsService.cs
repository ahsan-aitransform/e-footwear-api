using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Core.Contracts;
using EFootwear.BLL.Core.Settings;
using EFootwear.Domain.Common;
using EFootwear.Domain.Core.Settings;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Core.Services;

/// <summary>
/// The settings of the company (Business Info, regional preferences, printing, tax, inventory and promotion policies, payment methods)
/// and the per-store overrides of Business Info and Printing (docx 2.19, Part 7). Policy values stay null ("not configured") until a
/// person chooses them: nothing here invents a business rule. The company comes from the session, never from the request.
/// </summary>
public sealed class CompanySettingsService(
    ISettingsRepository settingsRepository,
    ICompanyProfileRepository companyProfileRepository,
    StoreAccessGuard storeAccessGuard,
    RequestContext requestContext,
    IUnitOfWork unitOfWork,
    ILogger<CompanySettingsService> logger)
{
    private static readonly BusinessInfoSettings NoBusinessInfo = new(null, null, null, null, null, null, null, null, null);
    private static readonly RegionalPreferencesSettings NoRegionalPreferences = new(null, null, null);
    private static readonly PrintingSettings NoPrinting = new(null, null, null, null, null, null, null);
    private static readonly TaxSettings NoTax = new(null, null);
    private static readonly InventoryPolicySettings NoInventoryPolicy = new(null, null, null);
    private static readonly PromotionPolicySettings NoPromotionPolicy = new(null);

    // ---------------- Business Info (company and store) ----------------

    public async Task<SettingsDocumentResponse<BusinessInfoSettings>> GetBusinessInfoAsync(CancellationToken cancellationToken)
    {
        var companyId = requestContext.CompanyId;
        var profile = await companyProfileRepository.FindAsync(companyId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "Company not found.");
        var stored = await settingsRepository.GetCompanySectionAsync(companyId, SettingsSectionCodes.BusinessInfo, cancellationToken);
        var values = stored is null ? NoBusinessInfo : SettingsJson.Deserialize<BusinessInfoSettings>(stored.ValuesJson);
        return new(values with { BusinessName = profile.Name, InvoiceText = profile.InvoiceText },
                   stored?.RowVersion ?? 0, stored is not null);
    }

    public async Task<SettingsDocumentResponse<BusinessInfoSettings>> SaveBusinessInfoAsync(
        SaveSettingsRequest<BusinessInfoSettings> request, CancellationToken cancellationToken)
    {
        var normalized = BusinessInfoSettingsRules.NormalizeForCompany(RequireValues(request.Values));
        var companyId = requestContext.CompanyId;

        // The business name and the invoice text live in platform.companies; everything else is a settings section.
        await companyProfileRepository.UpdateAsync(companyId, new Dictionary<string, string?>
        {
            ["name"] = normalized.BusinessName,
            ["invoice_terms"] = normalized.InvoiceText,
        }, expectedRowVersion: null, cancellationToken);
        await settingsRepository.SaveCompanySectionAsync(companyId, SettingsSectionCodes.BusinessInfo,
            SettingsJson.Serialize(normalized with { BusinessName = null, InvoiceText = null }), request.ExpectedRowVersion, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Business info of company {CompanyId} saved by user {UserId}", companyId, requestContext.UserId);
        return await GetBusinessInfoAsync(cancellationToken);
    }

    public async Task<StoreSettingsDocumentResponse<BusinessInfoSettings>> GetStoreBusinessInfoAsync(
        Guid storeId, CancellationToken cancellationToken)
    {
        await storeAccessGuard.EnsureAccessibleAsync(storeId, cancellationToken);
        var company = await GetBusinessInfoAsync(cancellationToken);
        var stored = await settingsRepository.GetStoreSectionAsync(requestContext.CompanyId, storeId, SettingsSectionCodes.BusinessInfo, cancellationToken);
        var overrides = stored is null ? NoBusinessInfo : SettingsJson.Deserialize<BusinessInfoSettings>(stored.ValuesJson);
        return new(overrides, BusinessInfoSettingsRules.Merge(company.Values, overrides), stored?.RowVersion ?? 0, stored is not null);
    }

    public async Task<StoreSettingsDocumentResponse<BusinessInfoSettings>> SaveStoreBusinessInfoAsync(
        Guid storeId, SaveSettingsRequest<BusinessInfoSettings> request, CancellationToken cancellationToken)
    {
        await storeAccessGuard.EnsureAccessibleAsync(storeId, cancellationToken);
        var normalized = BusinessInfoSettingsRules.NormalizeForStore(RequireValues(request.Values));
        await settingsRepository.SaveStoreSectionAsync(requestContext.CompanyId, storeId, SettingsSectionCodes.BusinessInfo,
            SettingsJson.Serialize(normalized), request.ExpectedRowVersion, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Business info overrides of store {StoreId} saved by user {UserId}", storeId, requestContext.UserId);
        return await GetStoreBusinessInfoAsync(storeId, cancellationToken);
    }

    // ---------------- Regional preferences, tax, inventory and promotion policy (company) ----------------

    public Task<SettingsDocumentResponse<RegionalPreferencesSettings>> GetRegionalPreferencesAsync(CancellationToken cancellationToken) =>
        ReadCompanySectionAsync(SettingsSectionCodes.RegionalPreferences, NoRegionalPreferences, cancellationToken);

    public async Task<SettingsDocumentResponse<RegionalPreferencesSettings>> SaveRegionalPreferencesAsync(
        SaveSettingsRequest<RegionalPreferencesSettings> request, CancellationToken cancellationToken)
    {
        await WriteCompanySectionAsync(SettingsSectionCodes.RegionalPreferences,
            RegionalPreferencesSettingsRules.Normalize(RequireValues(request.Values)), request.ExpectedRowVersion, cancellationToken);
        return await GetRegionalPreferencesAsync(cancellationToken);
    }

    public Task<SettingsDocumentResponse<TaxSettings>> GetTaxAsync(CancellationToken cancellationToken) =>
        ReadCompanySectionAsync(SettingsSectionCodes.Tax, NoTax, cancellationToken);

    public async Task<SettingsDocumentResponse<TaxSettings>> SaveTaxAsync(
        SaveSettingsRequest<TaxSettings> request, CancellationToken cancellationToken)
    {
        await WriteCompanySectionAsync(SettingsSectionCodes.Tax,
            TaxSettingsRules.Normalize(RequireValues(request.Values)), request.ExpectedRowVersion, cancellationToken);
        return await GetTaxAsync(cancellationToken);
    }

    public Task<SettingsDocumentResponse<InventoryPolicySettings>> GetInventoryPolicyAsync(CancellationToken cancellationToken) =>
        ReadCompanySectionAsync(SettingsSectionCodes.InventoryPolicy, NoInventoryPolicy, cancellationToken);

    public async Task<SettingsDocumentResponse<InventoryPolicySettings>> SaveInventoryPolicyAsync(
        SaveSettingsRequest<InventoryPolicySettings> request, CancellationToken cancellationToken)
    {
        await WriteCompanySectionAsync(SettingsSectionCodes.InventoryPolicy,
            InventoryPolicySettingsRules.Normalize(RequireValues(request.Values)), request.ExpectedRowVersion, cancellationToken);
        return await GetInventoryPolicyAsync(cancellationToken);
    }

    public Task<SettingsDocumentResponse<PromotionPolicySettings>> GetPromotionPolicyAsync(CancellationToken cancellationToken) =>
        ReadCompanySectionAsync(SettingsSectionCodes.PromotionPolicy, NoPromotionPolicy, cancellationToken);

    public async Task<SettingsDocumentResponse<PromotionPolicySettings>> SavePromotionPolicyAsync(
        SaveSettingsRequest<PromotionPolicySettings> request, CancellationToken cancellationToken)
    {
        await WriteCompanySectionAsync(SettingsSectionCodes.PromotionPolicy,
            PromotionPolicySettingsRules.Normalize(RequireValues(request.Values)), request.ExpectedRowVersion, cancellationToken);
        return await GetPromotionPolicyAsync(cancellationToken);
    }

    // ---------------- Printing (company default and store overrides) ----------------

    public Task<SettingsDocumentResponse<PrintingSettings>> GetPrintingAsync(CancellationToken cancellationToken) =>
        ReadCompanySectionAsync(SettingsSectionCodes.Printing, NoPrinting, cancellationToken);

    public async Task<SettingsDocumentResponse<PrintingSettings>> SavePrintingAsync(
        SaveSettingsRequest<PrintingSettings> request, CancellationToken cancellationToken)
    {
        await WriteCompanySectionAsync(SettingsSectionCodes.Printing,
            PrintingSettingsRules.Normalize(RequireValues(request.Values)), request.ExpectedRowVersion, cancellationToken);
        return await GetPrintingAsync(cancellationToken);
    }

    public async Task<StoreSettingsDocumentResponse<PrintingSettings>> GetStorePrintingAsync(Guid storeId, CancellationToken cancellationToken)
    {
        await storeAccessGuard.EnsureAccessibleAsync(storeId, cancellationToken);
        var company = await GetPrintingAsync(cancellationToken);
        var stored = await settingsRepository.GetStoreSectionAsync(requestContext.CompanyId, storeId, SettingsSectionCodes.Printing, cancellationToken);
        var overrides = stored is null ? NoPrinting : SettingsJson.Deserialize<PrintingSettings>(stored.ValuesJson);
        return new(overrides, PrintingSettingsRules.Merge(company.Values, overrides), stored?.RowVersion ?? 0, stored is not null);
    }

    public async Task<StoreSettingsDocumentResponse<PrintingSettings>> SaveStorePrintingAsync(
        Guid storeId, SaveSettingsRequest<PrintingSettings> request, CancellationToken cancellationToken)
    {
        await storeAccessGuard.EnsureAccessibleAsync(storeId, cancellationToken);
        var normalized = PrintingSettingsRules.Normalize(RequireValues(request.Values));
        await settingsRepository.SaveStoreSectionAsync(requestContext.CompanyId, storeId, SettingsSectionCodes.Printing,
            SettingsJson.Serialize(normalized), request.ExpectedRowVersion, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Printing overrides of store {StoreId} saved by user {UserId}", storeId, requestContext.UserId);
        return await GetStorePrintingAsync(storeId, cancellationToken);
    }

    // ---------------- Payment methods ----------------

    public async Task<SettingsDocumentResponse<PaymentMethodsSettings>> GetPaymentMethodsAsync(CancellationToken cancellationToken) =>
        await ReadCompanySectionAsync(SettingsSectionCodes.PaymentMethods, PaymentMethodsSettingsRules.DefaultWhenNotConfigured(), cancellationToken);

    public async Task<SettingsDocumentResponse<PaymentMethodsSettings>> SavePaymentMethodsAsync(
        SaveSettingsRequest<PaymentMethodsSettings> request, CancellationToken cancellationToken)
    {
        await WriteCompanySectionAsync(SettingsSectionCodes.PaymentMethods,
            PaymentMethodsSettingsRules.Normalize(RequireValues(request.Values)), request.ExpectedRowVersion, cancellationToken);
        return await GetPaymentMethodsAsync(cancellationToken);
    }

    /// <summary>The methods a cashier may choose. Every company user may read this list (sales screens need it).</summary>
    public async Task<IReadOnlyList<PaymentMethodSetting>> GetEnabledPaymentMethodsAsync(CancellationToken cancellationToken)
    {
        var document = await GetPaymentMethodsAsync(cancellationToken);
        return document.Values.Methods.Where(method => method.IsEnabled).ToList();
    }

    // ---------------- helpers ----------------

    private async Task<SettingsDocumentResponse<TValues>> ReadCompanySectionAsync<TValues>(
        string sectionCode, TValues whenNotConfigured, CancellationToken cancellationToken)
    {
        var stored = await settingsRepository.GetCompanySectionAsync(requestContext.CompanyId, sectionCode, cancellationToken);
        return stored is null
            ? new SettingsDocumentResponse<TValues>(whenNotConfigured, 0, false)
            : new SettingsDocumentResponse<TValues>(SettingsJson.Deserialize<TValues>(stored.ValuesJson), stored.RowVersion, true);
    }

    private async Task WriteCompanySectionAsync<TValues>(
        string sectionCode, TValues values, long? expectedRowVersion, CancellationToken cancellationToken)
    {
        await settingsRepository.SaveCompanySectionAsync(requestContext.CompanyId, sectionCode,
            SettingsJson.Serialize(values), expectedRowVersion, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Settings section {SectionCode} of company {CompanyId} saved by user {UserId}",
            sectionCode, requestContext.CompanyId, requestContext.UserId);
    }

    private static TValues RequireValues<TValues>(TValues? values) where TValues : class =>
        values ?? throw new DomainException(ErrorKind.Validation, "The settings values are missing.");
}
