namespace EFootwear.BLL.Core.Abstractions;

/// <summary>core.attachments: one row per stored file (the file itself is in <see cref="IFileStorage"/>). Rows are never deleted.</summary>
public interface IAttachmentRepository
{
    Task<AttachmentRecord> AddAsync(Guid companyId, string entityType, Guid entityId, string storageKey, string contentType,
                                    int sizeBytes, string sha256Hex, CancellationToken cancellationToken);

    Task<AttachmentRecord?> FindLatestAsync(Guid companyId, string entityType, Guid entityId, CancellationToken cancellationToken);
}
