namespace EFootwear.BLL.MasterData.Contracts;

/// <summary>Adds an item to a master list. The name is required and unique (without regard to case) inside the company.</summary>
public sealed record CreateMasterListItemRequest(string Name, string? NameUrdu = null, string? Code = null, int SortOrder = 0, bool IsActive = true);
