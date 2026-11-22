namespace EFootwear.BLL.Common;

/// <summary>One place that still uses a record: the table (for example "catalog.products") and how many rows point at it.</summary>
public sealed record RowUsageResponse(string TableName, long UsageCount);
