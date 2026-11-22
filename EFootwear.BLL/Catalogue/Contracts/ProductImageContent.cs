namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>An opened product picture. The caller (the controller) disposes the stream after sending it.</summary>
public sealed record ProductImageContent(Stream Content, string ContentType);
