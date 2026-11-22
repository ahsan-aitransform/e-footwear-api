namespace EFootwear.BLL.Platform.Contracts;

public sealed record CompanyExportResponse(Guid Id, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Status, long? SizeBytes, bool IsEncrypted);
