namespace EFootwear.BLL.MasterData.Contracts;

/// <summary>Changes an item (docx Edit button). RowVersion is the value the screen loaded; a stale value is refused with 409.</summary>
public sealed record UpdateMasterListItemRequest(string Name, string? NameUrdu, string? Code, int SortOrder, bool IsActive, long RowVersion);
