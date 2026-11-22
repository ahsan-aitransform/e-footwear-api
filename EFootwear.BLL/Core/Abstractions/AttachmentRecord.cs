namespace EFootwear.BLL.Core.Abstractions;

public sealed record AttachmentRecord(Guid Id, string StorageKey, string ContentType, int SizeBytes, DateTimeOffset CreatedAt);
