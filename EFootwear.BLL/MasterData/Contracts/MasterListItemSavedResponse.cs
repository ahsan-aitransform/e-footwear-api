namespace EFootwear.BLL.MasterData.Contracts;

/// <summary>
/// The answer to a save: the id of the item and, for lists that warn about spelling slips (cities), the existing names that look
/// like the one just saved. A warning never blocks the save (docx 2.17 improvement).
/// </summary>
public sealed record MasterListItemSavedResponse(Guid Id, IReadOnlyList<string> NearDuplicateNames);
