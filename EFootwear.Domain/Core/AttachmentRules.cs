using EFootwear.Domain.Common;

namespace EFootwear.Domain.Core;

/// <summary>Limits and checks for uploaded images. The size limit is technical (memory, receipts) and can be raised in one place.</summary>
public static class AttachmentRules
{
    public const int LogoMaximumSizeBytes = 1024 * 1024;
    public const string PngContentType = "image/png";
    public const string JpegContentType = "image/jpeg";
    public const string WebpContentType = "image/webp";

    public static readonly IReadOnlyList<string> AllowedLogoContentTypes = [PngContentType, JpegContentType, WebpContentType];

    /// <summary>
    /// Decides the real image type from the first bytes of the file. The type the client declares is never trusted
    /// (it could hide a script behind an image name). Returns null when the bytes are not PNG, JPEG or WEBP.
    /// </summary>
    public static string? DetectImageContentType(ReadOnlySpan<byte> firstBytes)
    {
        if (firstBytes.Length >= 8 && firstBytes[0] == 0x89 && firstBytes[1] == 0x50 && firstBytes[2] == 0x4E && firstBytes[3] == 0x47
            && firstBytes[4] == 0x0D && firstBytes[5] == 0x0A && firstBytes[6] == 0x1A && firstBytes[7] == 0x0A)
            return PngContentType;
        if (firstBytes.Length >= 3 && firstBytes[0] == 0xFF && firstBytes[1] == 0xD8 && firstBytes[2] == 0xFF)
            return JpegContentType;
        if (firstBytes.Length >= 12 && firstBytes[0] == 'R' && firstBytes[1] == 'I' && firstBytes[2] == 'F' && firstBytes[3] == 'F'
            && firstBytes[8] == 'W' && firstBytes[9] == 'E' && firstBytes[10] == 'B' && firstBytes[11] == 'P')
            return WebpContentType;
        return null;
    }

    public static string FileExtensionFor(string contentType) => contentType switch
    {
        PngContentType => ".png",
        JpegContentType => ".jpg",
        WebpContentType => ".webp",
        _ => throw new DomainException(ErrorKind.Validation, "Only PNG, JPEG or WEBP images are accepted."),
    };
}
