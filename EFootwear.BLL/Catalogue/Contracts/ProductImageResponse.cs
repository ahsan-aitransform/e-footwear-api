namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>A filled picture slot of a product. The picture itself is read from the image endpoint of the same slot.</summary>
public sealed record ProductImageResponse(int Slot);
