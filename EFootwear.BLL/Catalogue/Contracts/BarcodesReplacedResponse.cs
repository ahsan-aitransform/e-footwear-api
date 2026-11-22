namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>The result of Replace Barcodes. BatchId groups the history rows; WasAlreadyApplied is true when a retry found the earlier result.</summary>
public sealed record BarcodesReplacedResponse(Guid BatchId, int ReplacedCount, bool WasAlreadyApplied);
