using E_Footwear_Api.Routing;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Settings;

/// <summary>The URLs of the Settings and platform administration endpoints, built from ApiRoutes (never typed twice).</summary>
internal static class SettingsUrls
{
    public static string Settings(string segment) => TestScenarioBuilder.Url(ApiRoutes.CompanySettings.Root, segment);

    public static string StoreSection(string routeTemplate, Guid storeId) =>
        Settings(routeTemplate.Replace("{storeId:guid}", storeId.ToString()));

    public static string CompanyProfile => TestScenarioBuilder.Url(ApiRoutes.CompanySettings.Root, ApiRoutes.CompanySettings.CompanyProfile);
    public static string CompanyLogo => Settings(ApiRoutes.CompanySettings.CompanyLogo);
    public static string CompanyLogoInformation => Settings(ApiRoutes.CompanySettings.CompanyLogoInformation);

    public static string StoreManagement(string segment = "") => TestScenarioBuilder.Url(ApiRoutes.StoreManagement.Root, segment);

    public static string StoreManagementDetail(string routeTemplate, Guid storeId) =>
        StoreManagement(routeTemplate.Replace("{storeId:guid}", storeId.ToString()));

    public static string DocumentNumbering(Guid? storeId = null) =>
        TestScenarioBuilder.Url(ApiRoutes.DocumentNumbering.Root) + (storeId is null ? "" : $"?storeId={storeId}");

    public static string PosTerminals(Guid? storeId = null) =>
        TestScenarioBuilder.Url(ApiRoutes.PosTerminals.Root) + (storeId is null ? "" : $"?storeId={storeId}");

    public static string MyPreferences => TestScenarioBuilder.Url(ApiRoutes.UserPreferences.Root);

    public static string Backup(string segment) => TestScenarioBuilder.Url(ApiRoutes.Backup.Root, segment);

    public static string PlatformPlans(string segment = "") => TestScenarioBuilder.Url(ApiRoutes.PlatformPlans.Root, segment);

    public static string PlatformCompany(Guid companyId, string routeTemplate) =>
        TestScenarioBuilder.Url(ApiRoutes.PlatformCompanyAdministration.Root, routeTemplate.Replace("{companyId:guid}", companyId.ToString()));

    public static string PlatformAuditLog(string query = "") => TestScenarioBuilder.Url(ApiRoutes.PlatformAuditLog.Root) + query;

    public static string PlatformBackup => TestScenarioBuilder.Url(ApiRoutes.PlatformBackup.Root, ApiRoutes.PlatformBackup.Overview);
}
