using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Core.Contracts;
using EFootwear.BLL.Core.Services;
using EFootwear.Domain.Core.Settings;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Core;

/// <summary>
/// The settings sections of the company and the per-store overrides (docx 2.19 Preference, Part 7). Reading and saving a section
/// need the Preference permission; payment methods have their own screen. The list of enabled payment methods is for every company
/// user because the sales screens need it.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.CompanySettings.Root)]
public sealed class CompanySettingsController(CompanySettingsService companySettingsService) : ControllerBase
{
    [HttpGet(ApiRoutes.CompanySettings.BusinessInfo)]
    [RequirePermission(PermissionCodes.Settings.Preference.View)]
    public async Task<ActionResult<SettingsDocumentResponse<BusinessInfoSettings>>> GetBusinessInfo(CancellationToken cancellationToken) =>
        await companySettingsService.GetBusinessInfoAsync(cancellationToken);

    [HttpPut(ApiRoutes.CompanySettings.BusinessInfo)]
    [RequirePermission(PermissionCodes.Settings.Preference.Edit)]
    public async Task<ActionResult<SettingsDocumentResponse<BusinessInfoSettings>>> SaveBusinessInfo(
        SaveSettingsRequest<BusinessInfoSettings> request, CancellationToken cancellationToken) =>
        await companySettingsService.SaveBusinessInfoAsync(request, cancellationToken);

    [HttpGet(ApiRoutes.CompanySettings.StoreBusinessInfo)]
    [RequirePermission(PermissionCodes.Settings.Preference.View)]
    public async Task<ActionResult<StoreSettingsDocumentResponse<BusinessInfoSettings>>> GetStoreBusinessInfo(
        Guid storeId, CancellationToken cancellationToken) =>
        await companySettingsService.GetStoreBusinessInfoAsync(storeId, cancellationToken);

    [HttpPut(ApiRoutes.CompanySettings.StoreBusinessInfo)]
    [RequirePermission(PermissionCodes.Settings.Preference.Edit)]
    public async Task<ActionResult<StoreSettingsDocumentResponse<BusinessInfoSettings>>> SaveStoreBusinessInfo(
        Guid storeId, SaveSettingsRequest<BusinessInfoSettings> request, CancellationToken cancellationToken) =>
        await companySettingsService.SaveStoreBusinessInfoAsync(storeId, request, cancellationToken);

    [HttpGet(ApiRoutes.CompanySettings.RegionalPreferences)]
    [RequirePermission(PermissionCodes.Settings.Preference.View)]
    public async Task<ActionResult<SettingsDocumentResponse<RegionalPreferencesSettings>>> GetRegionalPreferences(
        CancellationToken cancellationToken) =>
        await companySettingsService.GetRegionalPreferencesAsync(cancellationToken);

    [HttpPut(ApiRoutes.CompanySettings.RegionalPreferences)]
    [RequirePermission(PermissionCodes.Settings.Preference.Edit)]
    public async Task<ActionResult<SettingsDocumentResponse<RegionalPreferencesSettings>>> SaveRegionalPreferences(
        SaveSettingsRequest<RegionalPreferencesSettings> request, CancellationToken cancellationToken) =>
        await companySettingsService.SaveRegionalPreferencesAsync(request, cancellationToken);

    [HttpGet(ApiRoutes.CompanySettings.Printing)]
    [RequirePermission(PermissionCodes.Settings.Preference.View)]
    public async Task<ActionResult<SettingsDocumentResponse<PrintingSettings>>> GetPrinting(CancellationToken cancellationToken) =>
        await companySettingsService.GetPrintingAsync(cancellationToken);

    [HttpPut(ApiRoutes.CompanySettings.Printing)]
    [RequirePermission(PermissionCodes.Settings.Preference.Edit)]
    public async Task<ActionResult<SettingsDocumentResponse<PrintingSettings>>> SavePrinting(
        SaveSettingsRequest<PrintingSettings> request, CancellationToken cancellationToken) =>
        await companySettingsService.SavePrintingAsync(request, cancellationToken);

    [HttpGet(ApiRoutes.CompanySettings.StorePrinting)]
    [RequirePermission(PermissionCodes.Settings.Preference.View)]
    public async Task<ActionResult<StoreSettingsDocumentResponse<PrintingSettings>>> GetStorePrinting(
        Guid storeId, CancellationToken cancellationToken) =>
        await companySettingsService.GetStorePrintingAsync(storeId, cancellationToken);

    [HttpPut(ApiRoutes.CompanySettings.StorePrinting)]
    [RequirePermission(PermissionCodes.Settings.Preference.Edit)]
    public async Task<ActionResult<StoreSettingsDocumentResponse<PrintingSettings>>> SaveStorePrinting(
        Guid storeId, SaveSettingsRequest<PrintingSettings> request, CancellationToken cancellationToken) =>
        await companySettingsService.SaveStorePrintingAsync(storeId, request, cancellationToken);

    [HttpGet(ApiRoutes.CompanySettings.Tax)]
    [RequirePermission(PermissionCodes.Settings.Preference.View)]
    public async Task<ActionResult<SettingsDocumentResponse<TaxSettings>>> GetTax(CancellationToken cancellationToken) =>
        await companySettingsService.GetTaxAsync(cancellationToken);

    [HttpPut(ApiRoutes.CompanySettings.Tax)]
    [RequirePermission(PermissionCodes.Settings.Preference.Edit)]
    public async Task<ActionResult<SettingsDocumentResponse<TaxSettings>>> SaveTax(
        SaveSettingsRequest<TaxSettings> request, CancellationToken cancellationToken) =>
        await companySettingsService.SaveTaxAsync(request, cancellationToken);

    [HttpGet(ApiRoutes.CompanySettings.InventoryPolicy)]
    [RequirePermission(PermissionCodes.Settings.Preference.View)]
    public async Task<ActionResult<SettingsDocumentResponse<InventoryPolicySettings>>> GetInventoryPolicy(
        CancellationToken cancellationToken) =>
        await companySettingsService.GetInventoryPolicyAsync(cancellationToken);

    [HttpPut(ApiRoutes.CompanySettings.InventoryPolicy)]
    [RequirePermission(PermissionCodes.Settings.Preference.Edit)]
    public async Task<ActionResult<SettingsDocumentResponse<InventoryPolicySettings>>> SaveInventoryPolicy(
        SaveSettingsRequest<InventoryPolicySettings> request, CancellationToken cancellationToken) =>
        await companySettingsService.SaveInventoryPolicyAsync(request, cancellationToken);

    [HttpGet(ApiRoutes.CompanySettings.PromotionPolicy)]
    [RequirePermission(PermissionCodes.Settings.Preference.View)]
    public async Task<ActionResult<SettingsDocumentResponse<PromotionPolicySettings>>> GetPromotionPolicy(
        CancellationToken cancellationToken) =>
        await companySettingsService.GetPromotionPolicyAsync(cancellationToken);

    [HttpPut(ApiRoutes.CompanySettings.PromotionPolicy)]
    [RequirePermission(PermissionCodes.Settings.Preference.Edit)]
    public async Task<ActionResult<SettingsDocumentResponse<PromotionPolicySettings>>> SavePromotionPolicy(
        SaveSettingsRequest<PromotionPolicySettings> request, CancellationToken cancellationToken) =>
        await companySettingsService.SavePromotionPolicyAsync(request, cancellationToken);

    [HttpGet(ApiRoutes.CompanySettings.PaymentMethods)]
    [RequirePermission(PermissionCodes.Settings.PaymentMethods.View)]
    public async Task<ActionResult<SettingsDocumentResponse<PaymentMethodsSettings>>> GetPaymentMethods(CancellationToken cancellationToken) =>
        await companySettingsService.GetPaymentMethodsAsync(cancellationToken);

    [HttpPut(ApiRoutes.CompanySettings.PaymentMethods)]
    [RequirePermission(PermissionCodes.Settings.PaymentMethods.Edit)]
    public async Task<ActionResult<SettingsDocumentResponse<PaymentMethodsSettings>>> SavePaymentMethods(
        SaveSettingsRequest<PaymentMethodsSettings> request, CancellationToken cancellationToken) =>
        await companySettingsService.SavePaymentMethodsAsync(request, cancellationToken);

    /// <summary>The methods a cashier may choose. Any company user (no screen permission): the sales screens read it.</summary>
    [HttpGet(ApiRoutes.CompanySettings.EnabledPaymentMethods)]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodSetting>>> GetEnabledPaymentMethods(CancellationToken cancellationToken) =>
        Ok(await companySettingsService.GetEnabledPaymentMethodsAsync(cancellationToken));
}
