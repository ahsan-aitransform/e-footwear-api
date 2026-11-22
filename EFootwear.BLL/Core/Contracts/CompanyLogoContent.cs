namespace EFootwear.BLL.Core.Contracts;

/// <summary>The current logo file. The caller disposes <see cref="Content"/>.</summary>
public sealed record CompanyLogoContent(Stream Content, string ContentType, DateTimeOffset UploadedAt);
