namespace EFootwear.BLL.HumanResources.Contracts;

/// <summary>One move of a staff member between stores: previous store, new store, effective date and the user who authorised it (Part 16.1).</summary>
public sealed record StaffTransferHistoryItemResponse(
    Guid Id,
    Guid? FromStoreId,
    string? FromStoreName,
    Guid ToStoreId,
    string ToStoreName,
    DateOnly EffectiveOn,
    Guid? ChangedByUserId,
    string? ChangedByName,
    DateTimeOffset ChangedAt);
