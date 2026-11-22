namespace EFootwear.BLL.MasterData.Contracts;

/// <summary>Adds or changes a size of a size group. The size code is unique inside the group.</summary>
public sealed record SaveSizeRequest(string SizeCode, int SortOrder = 0);
