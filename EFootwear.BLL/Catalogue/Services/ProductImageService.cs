using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Core.Abstractions;
using EFootwear.Domain.Catalogue;
using EFootwear.Domain.Common;
using EFootwear.Domain.Core;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Catalogue.Services;

/// <summary>
/// The Images tab of the New Product screen (docx 4.1.4): up to four pictures per product. As for the company logo, the bytes are checked, not the
/// declared type: size limit and the real image type from the first bytes. The storage key is built here (it starts with the company id), never by
/// the client. Replacing a picture leaves the old file in storage (IFileStorage has no delete); the slot points at the new one.
/// </summary>
public sealed class ProductImageService(
    IFileStorage fileStorage,
    IProductImageRepository productImageRepository,
    IProductRepository productRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<ProductImageService> logger)
{
    public async Task<ProductImageResponse> UploadAsync(Guid productId, int slot, Stream content, CancellationToken cancellationToken)
    {
        ProductImageRules.EnsureValidSlot(slot);
        _ = await productRepository.FindDetailAsync(productId, cancellationToken) ?? throw new DomainException(ErrorKind.NotFound, "The product was not found.");

        var buffer = new byte[ProductImageRules.MaximumSizeBytes + 1];
        var totalBytes = 0;
        while (totalBytes < buffer.Length)
        {
            var readBytes = await content.ReadAsync(buffer.AsMemory(totalBytes), cancellationToken);
            if (readBytes == 0) break;
            totalBytes += readBytes;
        }
        if (totalBytes == 0)
            throw new DomainException(ErrorKind.Validation, "The file is empty.");
        if (totalBytes > ProductImageRules.MaximumSizeBytes)
            throw new DomainException(ErrorKind.Validation, $"A picture can be at most {ProductImageRules.MaximumSizeBytes / 1024} KB.", ErrorCodes.FileTooLarge);
        var contentType = AttachmentRules.DetectImageContentType(buffer.AsSpan(0, totalBytes))
                          ?? throw new DomainException(ErrorKind.Validation, "Only PNG, JPEG or WEBP images are accepted.", ErrorCodes.UnsupportedFileType);

        var storageKey = $"companies/{currentUser.CompanyId:N}/products/{productId:N}/{slot}-{Guid.NewGuid():N}{AttachmentRules.FileExtensionFor(contentType)}";
        await using (var stream = new MemoryStream(buffer, 0, totalBytes, writable: false))
            await fileStorage.SaveAsync(storageKey, stream, cancellationToken);
        await productImageRepository.SetSlotAsync(productId, slot, storageKey, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Picture {Slot} of product {ProductId} set by user {UserId} ({SizeBytes} bytes, {ContentType})", slot, productId, currentUser.UserId, totalBytes, contentType);
        return new ProductImageResponse(slot);
    }

    public async Task<ProductImageContent?> OpenAsync(Guid productId, int slot, CancellationToken cancellationToken)
    {
        ProductImageRules.EnsureValidSlot(slot);
        var storageKey = await productImageRepository.FindStorageKeyAsync(productId, slot, cancellationToken);
        if (storageKey is null) return null;
        var stream = await fileStorage.OpenReadAsync(storageKey, cancellationToken);
        return stream is null ? null : new ProductImageContent(stream, ContentTypeFor(storageKey));
    }

    public async Task ClearAsync(Guid productId, int slot, CancellationToken cancellationToken)
    {
        ProductImageRules.EnsureValidSlot(slot);
        _ = await productRepository.FindDetailAsync(productId, cancellationToken) ?? throw new DomainException(ErrorKind.NotFound, "The product was not found.");
        await productImageRepository.ClearSlotAsync(productId, slot, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    private static string ContentTypeFor(string storageKey) => Path.GetExtension(storageKey).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg",
    };
}
