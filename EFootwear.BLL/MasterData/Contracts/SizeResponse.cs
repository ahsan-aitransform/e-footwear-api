namespace EFootwear.BLL.MasterData.Contracts;

/// <summary>A size inside a size group (for example "40").</summary>
public sealed record SizeResponse(Guid Id, Guid SizeGroupId, string SizeCode, int SortOrder);
