namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>The result of Change Product Attributes. SkippedProducts explain products that could not be changed (for example a colour the size already has).</summary>
public sealed record AttributeChangeAppliedResponse(
    int ChangedProductCount,
    IReadOnlyList<string> SkippedProducts);
