namespace EFootwear.BLL.Promotions.Contracts;

/// <summary>How many sizes were added and how many were skipped because the offer already had them.</summary>
public sealed record AddSpecialOfferLinesResponse(int AddedCount, int SkippedCount);
