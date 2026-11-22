using System.Net;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Settings;

/// <summary>Backup and restore information. The backups themselves run outside the application; these screens report what was recorded.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class BackupStatusApiTests(ApiTestEnvironment environment)
{
    private static readonly string Sha256 = new('a', 64);

    private async Task<Guid> RecordRunAsync(string kind, Guid? companyId, string location, string status = "OK")
    {
        var runId = Guid.NewGuid();
        await environment.Database.ExecuteAsOwnerAsync(
            """
            insert into ops.backup_runs (id, kind, company_id, started_at, finished_at, status, storage_location, size_bytes, sha256, encrypted)
            values ($1, $2, $3, now() - interval '1 hour', now(), $4, $5, 2048, $6, true)
            """, runId, kind, companyId, status, location, Sha256);
        return runId;
    }

    [Fact]
    public async Task A_company_sees_the_backup_health_and_its_own_exports_but_never_a_storage_location()
    {
        var first = await SettingsScenario.CreateAsync(environment);
        var second = await SettingsScenario.CreateAsync(environment);
        await RecordRunAsync("BASE", null, "offsite://bucket/base-1");
        await RecordRunAsync("LOGICAL_EXPORT", first.Company.CompanyId, "offsite://bucket/first-export");
        await RecordRunAsync("LOGICAL_EXPORT", second.Company.CompanyId, "offsite://bucket/second-export");

        var overview = await (await first.OwnerClient.GetAsync(SettingsUrls.Backup(ApiRoutes.Backup.Overview))).ReadSuccessJsonAsync();
        Assert.Equal("OK", overview.GetProperty("health").GetProperty("baseBackupStatus").GetString());
        var exports = overview.GetProperty("exports");
        Assert.Equal(1, exports.GetArrayLength());
        Assert.Equal("OK", exports[0].GetProperty("status").GetString());
        Assert.True(exports[0].GetProperty("isEncrypted").GetBoolean());
        Assert.DoesNotContain("offsite://", overview.GetRawText());
    }

    [Fact]
    public async Task The_platform_administrator_sees_every_run_with_its_location_and_the_restore_tests()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        var location = "offsite://bucket/" + Guid.NewGuid().ToString("N");
        var runId = await RecordRunAsync("BASE", null, location);
        await environment.Database.ExecuteAsOwnerAsync(
            """
            insert into ops.restore_drills (backup_run_id, finished_at, outcome, measured_rto_minutes, measured_rpo_minutes, rows_checked, checks_performed, performed_by)
            values ($1, now(), 'PASS', 12.5, 5, 1000, 'trial balance totals', 'tester')
            """, runId);

        var overview = await (await scenario.PlatformClient.GetAsync(SettingsUrls.PlatformBackup)).ReadSuccessJsonAsync();
        Assert.Contains(overview.GetProperty("runs").EnumerateArray(), run => run.GetProperty("storageLocation").GetString() == location);
        var drill = overview.GetProperty("drills").EnumerateArray().First();
        Assert.Equal("PASS", drill.GetProperty("outcome").GetString());

        var restore = await (await scenario.OwnerClient.GetAsync(SettingsUrls.Backup(ApiRoutes.Backup.RestoreOverview))).ReadSuccessJsonAsync();
        Assert.True(restore.GetProperty("drills").GetArrayLength() >= 1);
        Assert.DoesNotContain("tester", restore.GetRawText());   // who ran it is not shown to a company
        Assert.DoesNotContain("offsite://", restore.GetRawText());
    }

    [Fact]
    public async Task Backup_screens_need_their_permissions_and_the_platform_overview_is_for_administrators_only()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        var backupViewer = await scenario.CreateUserClientAsync("STORE_MANAGER", [scenario.Company.FirstStoreId], PermissionCodes.Settings.Backup.View);
        var restoreViewer = await scenario.CreateUserClientAsync("STORE_MANAGER", [scenario.Company.FirstStoreId], PermissionCodes.Settings.Restore.View);

        Assert.Equal(HttpStatusCode.OK, (await backupViewer.GetAsync(SettingsUrls.Backup(ApiRoutes.Backup.Overview))).StatusCode);
        await (await backupViewer.GetAsync(SettingsUrls.Backup(ApiRoutes.Backup.RestoreOverview))).AssertProblemAsync(HttpStatusCode.Forbidden, "PERMISSION_DENIED");
        Assert.Equal(HttpStatusCode.OK, (await restoreViewer.GetAsync(SettingsUrls.Backup(ApiRoutes.Backup.RestoreOverview))).StatusCode);
        await (await restoreViewer.GetAsync(SettingsUrls.Backup(ApiRoutes.Backup.Overview))).AssertProblemAsync(HttpStatusCode.Forbidden, "PERMISSION_DENIED");

        await (await scenario.OwnerClient.GetAsync(SettingsUrls.PlatformBackup)).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await scenario.PlatformClient.GetAsync(SettingsUrls.Backup(ApiRoutes.Backup.Overview))).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }
}
