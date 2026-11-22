using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Core.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Core.Repositories;

/// <summary>core.number_series with raw SQL. A series only ever moves forward, also at database level.</summary>
public sealed class DocumentNumberingRepository(DatabaseSession databaseSession) : IDocumentNumberingRepository
{
    public async Task<IReadOnlyList<DocumentNumberSeriesResponse>> ListForStoreAsync(Guid companyId, Guid storeId,
                                                                                      CancellationToken cancellationToken)
    {
        var databaseContext = await GetDatabaseContextAsync(companyId, cancellationToken);
        var rows = await databaseContext.Database.SqlQuery<SeriesRow>($"""
            select store_id as "StoreId", series_code as "SeriesCode", period_key as "PeriodKey", next_value as "NextValue"
              from core.number_series
             where company_id = {companyId} and store_id = {storeId}
            """).ToListAsync(cancellationToken);
        return rows.OrderBy(row => row.SeriesCode).ThenBy(row => row.PeriodKey)
                   .Select(row => new DocumentNumberSeriesResponse(row.StoreId, row.SeriesCode, row.PeriodKey, row.NextValue, true))
                   .ToList();
    }

    public async Task<long?> GetNextValueAsync(Guid companyId, Guid storeId, string seriesCode, DateOnly periodKey,
                                               CancellationToken cancellationToken)
    {
        var databaseContext = await GetDatabaseContextAsync(companyId, cancellationToken);
        var values = await databaseContext.Database.SqlQuery<long>($"""
            select next_value as "Value" from core.number_series
             where company_id = {companyId} and store_id = {storeId} and series_code = {seriesCode} and period_key = {periodKey}
            """).ToListAsync(cancellationToken);
        return values.Count == 0 ? null : values[0];
    }

    public async Task SetNextValueAsync(Guid companyId, Guid storeId, string seriesCode, DateOnly periodKey, long nextValue,
                                        CancellationToken cancellationToken)
    {
        var databaseContext = await GetDatabaseContextAsync(companyId, cancellationToken);
        var affectedRows = await databaseContext.Database.ExecuteSqlAsync($"""
            insert into core.number_series as existing (company_id, store_id, series_code, period_key, next_value)
            values ({companyId}, {storeId}, {seriesCode}, {periodKey}, {nextValue})
            on conflict (company_id, store_id, series_code, period_key) do update
                set next_value = excluded.next_value
              where existing.next_value <= excluded.next_value
            """, cancellationToken);
        if (affectedRows == 0)
            throw new DomainException(ErrorKind.Conflict,
                "Documents were numbered in the meantime; reload the series and choose a higher number.",
                ErrorCodes.DocumentNumberWouldRepeat);
    }

    private async Task<ApplicationDatabaseContext> GetDatabaseContextAsync(Guid companyId, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        return await databaseSession.GetDatabaseContextAsync(cancellationToken);
    }

    private sealed class SeriesRow
    {
        public Guid StoreId { get; set; }
        public string SeriesCode { get; set; } = "";
        public DateOnly PeriodKey { get; set; }
        public long NextValue { get; set; }
    }
}
