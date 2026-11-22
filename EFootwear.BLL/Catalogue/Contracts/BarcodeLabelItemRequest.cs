namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>One size and how many labels of it to print.</summary>
public sealed record BarcodeLabelItemRequest(Guid VariantId, int Copies);
