namespace EFootwear.BLL.Core.Contracts;

/// <summary>A document number series of a store. IsStored is false for a series that was never used (its next number is 1).</summary>
public sealed record DocumentNumberSeriesResponse(Guid StoreId, string SeriesCode, DateOnly PeriodKey, long NextValue, bool IsStored);
