namespace EFootwear.BLL.Core.Contracts;

public sealed record CompanyLogoResponse(Guid AttachmentId, string ContentType, int SizeBytes, DateTimeOffset UploadedAt);
