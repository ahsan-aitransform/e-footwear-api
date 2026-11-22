using System.Security.Cryptography;
using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Core.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Core;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Core.Services;

/// <summary>
/// The company logo (ADD-07-01). The uploaded bytes are checked, not the declared type: size limit, and the real image type is
/// read from the file's first bytes. A new upload becomes the current logo; older files stay as history (rows are never deleted).
/// </summary>
public sealed class CompanyLogoService(
    IFileStorage fileStorage,
    IAttachmentRepository attachmentRepository,
    RequestContext requestContext,
    IUnitOfWork unitOfWork,
    ILogger<CompanyLogoService> logger)
{
    public async Task<CompanyLogoResponse> UploadAsync(Stream content, CancellationToken cancellationToken)
    {
        var buffer = new byte[AttachmentRules.LogoMaximumSizeBytes + 1];
        var totalBytes = 0;
        while (totalBytes < buffer.Length)
        {
            var readBytes = await content.ReadAsync(buffer.AsMemory(totalBytes), cancellationToken);
            if (readBytes == 0) break;
            totalBytes += readBytes;
        }
        if (totalBytes == 0)
            throw new DomainException(ErrorKind.Validation, "The file is empty.");
        if (totalBytes > AttachmentRules.LogoMaximumSizeBytes)
            throw new DomainException(ErrorKind.Validation,
                $"The logo can be at most {AttachmentRules.LogoMaximumSizeBytes / 1024} KB.", ErrorCodes.FileTooLarge);

        var contentType = AttachmentRules.DetectImageContentType(buffer.AsSpan(0, totalBytes))
            ?? throw new DomainException(ErrorKind.Validation, "Only PNG, JPEG or WEBP images are accepted.", ErrorCodes.UnsupportedFileType);

        var companyId = requestContext.CompanyId;
        // The storage key is built here, never taken from the client, and always starts with the company id.
        var storageKey = $"companies/{companyId:N}/logo/{Guid.NewGuid():N}{AttachmentRules.FileExtensionFor(contentType)}";
        var sha256 = Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, totalBytes))).ToLowerInvariant();

        await using (var stream = new MemoryStream(buffer, 0, totalBytes, writable: false))
            await fileStorage.SaveAsync(storageKey, stream, cancellationToken);
        var record = await attachmentRepository.AddAsync(companyId, AttachmentEntityTypes.CompanyLogo, companyId,
            storageKey, contentType, totalBytes, sha256, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Logo of company {CompanyId} replaced by user {UserId} ({SizeBytes} bytes, {ContentType})",
            companyId, requestContext.UserId, totalBytes, contentType);
        return new CompanyLogoResponse(record.Id, record.ContentType, record.SizeBytes, record.CreatedAt);
    }

    public async Task<CompanyLogoResponse?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var record = await attachmentRepository.FindLatestAsync(requestContext.CompanyId, AttachmentEntityTypes.CompanyLogo,
            requestContext.CompanyId, cancellationToken);
        return record is null ? null : new CompanyLogoResponse(record.Id, record.ContentType, record.SizeBytes, record.CreatedAt);
    }

    public async Task<CompanyLogoContent?> OpenCurrentAsync(CancellationToken cancellationToken)
    {
        var record = await attachmentRepository.FindLatestAsync(requestContext.CompanyId, AttachmentEntityTypes.CompanyLogo,
            requestContext.CompanyId, cancellationToken);
        if (record is null) return null;
        var stream = await fileStorage.OpenReadAsync(record.StorageKey, cancellationToken);
        return stream is null ? null : new CompanyLogoContent(stream, record.ContentType, record.CreatedAt);
    }
}
