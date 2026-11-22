using EFootwear.BLL.Core.Abstractions;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Core.Repositories;

/// <summary>
/// Settings sections as JSON documents. A save is one upsert: when an expected row version is given and the stored version differs,
/// the update is skipped (0 rows) and a CONCURRENT_UPDATE conflict is raised, so nobody overwrites a change they never saw.
/// Row-level security restricts every statement to the caller's company (and, for store sections, to the caller's stores).
/// </summary>
public sealed class SettingsRepository(DatabaseSession databaseSession) : ISettingsRepository
{
    public async Task<StoredSettingsSection?> GetCompanySectionAsync(Guid companyId, string sectionCode, CancellationToken cancellationToken)
    {
        var databaseContext = await GetDatabaseContextAsync(companyId, cancellationToken);
        var rows = await databaseContext.Database.SqlQuery<StoredSectionRow>($"""
            select setting_values::text as "ValuesJson", row_ver as "RowVersion"
              from core.company_settings
             where company_id = {companyId} and section_code = {sectionCode}
            """).ToListAsync(cancellationToken);
        return rows.Select(row => new StoredSettingsSection(row.ValuesJson, row.RowVersion)).FirstOrDefault();
    }

    public async Task SaveCompanySectionAsync(Guid companyId, string sectionCode, string valuesJson, long? expectedRowVersion,
                                              CancellationToken cancellationToken)
    {
        var databaseContext = await GetDatabaseContextAsync(companyId, cancellationToken);
        var affectedRows = await databaseContext.Database.ExecuteSqlAsync($"""
            insert into core.company_settings as existing (company_id, section_code, setting_values)
            values ({companyId}, {sectionCode}, {valuesJson}::jsonb)
            on conflict (company_id, section_code) do update
                set setting_values = excluded.setting_values
              where {expectedRowVersion}::bigint is null or existing.row_ver = {expectedRowVersion}::bigint
            """, cancellationToken);
        EnsureSaved(affectedRows);
    }

    public async Task<StoredSettingsSection?> GetStoreSectionAsync(Guid companyId, Guid storeId, string sectionCode,
                                                                   CancellationToken cancellationToken)
    {
        var databaseContext = await GetDatabaseContextAsync(companyId, cancellationToken);
        var rows = await databaseContext.Database.SqlQuery<StoredSectionRow>($"""
            select setting_values::text as "ValuesJson", row_ver as "RowVersion"
              from core.store_settings
             where company_id = {companyId} and store_id = {storeId} and section_code = {sectionCode}
            """).ToListAsync(cancellationToken);
        return rows.Select(row => new StoredSettingsSection(row.ValuesJson, row.RowVersion)).FirstOrDefault();
    }

    public async Task SaveStoreSectionAsync(Guid companyId, Guid storeId, string sectionCode, string valuesJson, long? expectedRowVersion,
                                            CancellationToken cancellationToken)
    {
        var databaseContext = await GetDatabaseContextAsync(companyId, cancellationToken);
        var affectedRows = await databaseContext.Database.ExecuteSqlAsync($"""
            insert into core.store_settings as existing (company_id, store_id, section_code, setting_values)
            values ({companyId}, {storeId}, {sectionCode}, {valuesJson}::jsonb)
            on conflict (company_id, store_id, section_code) do update
                set setting_values = excluded.setting_values
              where {expectedRowVersion}::bigint is null or existing.row_ver = {expectedRowVersion}::bigint
            """, cancellationToken);
        EnsureSaved(affectedRows);
    }

    public async Task<StoredSettingsSection?> GetUserPreferencesAsync(Guid companyId, Guid userId, CancellationToken cancellationToken)
    {
        var databaseContext = await GetDatabaseContextAsync(companyId, cancellationToken);
        var rows = await databaseContext.Database.SqlQuery<StoredSectionRow>($"""
            select setting_values::text as "ValuesJson", row_ver as "RowVersion"
              from core.user_preferences
             where company_id = {companyId} and user_id = {userId}
            """).ToListAsync(cancellationToken);
        return rows.Select(row => new StoredSettingsSection(row.ValuesJson, row.RowVersion)).FirstOrDefault();
    }

    public async Task SaveUserPreferencesAsync(Guid companyId, Guid userId, string valuesJson, long? expectedRowVersion,
                                               CancellationToken cancellationToken)
    {
        var databaseContext = await GetDatabaseContextAsync(companyId, cancellationToken);
        var affectedRows = await databaseContext.Database.ExecuteSqlAsync($"""
            insert into core.user_preferences as existing (company_id, user_id, setting_values)
            values ({companyId}, {userId}, {valuesJson}::jsonb)
            on conflict (company_id, user_id) do update
                set setting_values = excluded.setting_values
              where {expectedRowVersion}::bigint is null or existing.row_ver = {expectedRowVersion}::bigint
            """, cancellationToken);
        EnsureSaved(affectedRows);
    }

    private async Task<ApplicationDatabaseContext> GetDatabaseContextAsync(Guid companyId, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        return await databaseSession.GetDatabaseContextAsync(cancellationToken);
    }

    private static void EnsureSaved(int affectedRows)
    {
        if (affectedRows == 0)
            throw new DomainException(ErrorKind.Conflict,
                "These settings were changed by someone else. Reload them and try again.", ErrorCodes.ConcurrentUpdate);
    }

    private sealed class StoredSectionRow
    {
        public string ValuesJson { get; set; } = "{}";
        public long RowVersion { get; set; }
    }
}
