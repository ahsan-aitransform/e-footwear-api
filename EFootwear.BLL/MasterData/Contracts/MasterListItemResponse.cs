namespace EFootwear.BLL.MasterData.Contracts;

/// <summary>A row of a master list screen (docx common pattern: Id, Name). RowVersion is sent back on update to detect a lost update.</summary>
public sealed record MasterListItemResponse(Guid Id, string? Code, string Name, string? NameUrdu, int SortOrder, bool IsActive, long RowVersion);
