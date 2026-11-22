using EFootwear.BLL.Core.Abstractions;
using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Core.Repositories;

/// <summary>core.attachments. The application role cannot delete rows, so the newest row of an entity is its current file.</summary>
public sealed class AttachmentRepository(DatabaseSession databaseSession) : IAttachmentRepository
{
    public async Task<AttachmentRecord> AddAsync(Guid companyId, string entityType, Guid entityId, string storageKey, string contentType,
                                                 int sizeBytes, string sha256Hex, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var attachmentId = Guid.CreateVersion7();
        await databaseContext.Database.ExecuteSqlAsync($"""
            insert into core.attachments (company_id, id, entity_type, entity_id, storage_key, mime_type, size_bytes, sha256, created_by)
            values ({companyId}, {attachmentId}, {entityType}, {entityId}, {storageKey}, {contentType}, {sizeBytes}, {sha256Hex},
                    core.current_user_id())
            """, cancellationToken);
        var rows = await databaseContext.Database.SqlQuery<AttachmentRow>($"""
            select id as "Id", storage_key as "StorageKey", mime_type as "ContentType", size_bytes as "SizeBytes", created_at as "CreatedAt"
              from core.attachments where company_id = {companyId} and id = {attachmentId}
            """).ToListAsync(cancellationToken);
        return ToRecord(rows.Single());
    }

    public async Task<AttachmentRecord?> FindLatestAsync(Guid companyId, string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var rows = await databaseContext.Database.SqlQuery<AttachmentRow>($"""
            select id as "Id", storage_key as "StorageKey", mime_type as "ContentType", size_bytes as "SizeBytes", created_at as "CreatedAt"
              from core.attachments
             where company_id = {companyId} and entity_type = {entityType} and entity_id = {entityId}
            """).ToListAsync(cancellationToken);
        var latest = rows.OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id).FirstOrDefault();
        return latest is null ? null : ToRecord(latest);
    }

    private static AttachmentRecord ToRecord(AttachmentRow row) => new(row.Id, row.StorageKey, row.ContentType, row.SizeBytes, row.CreatedAt);

    private sealed class AttachmentRow
    {
        public Guid Id { get; set; }
        public string StorageKey { get; set; } = "";
        public string ContentType { get; set; } = "";
        public int SizeBytes { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}
