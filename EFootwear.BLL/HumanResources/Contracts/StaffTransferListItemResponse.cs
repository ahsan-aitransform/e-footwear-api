namespace EFootwear.BLL.HumanResources.Contracts;

/// <summary>One row of the Staff Transfers screen: who moved, from which store to which, from which date, authorised by whom.</summary>
public sealed record StaffTransferListItemResponse(
    Guid Id,
    Guid PartyId,
    string StaffCode,
    string StaffName,
    Guid? FromStoreId,
    string? FromStoreName,
    Guid ToStoreId,
    string ToStoreName,
    DateOnly EffectiveOn,
    Guid? ChangedByUserId,
    string? ChangedByName,
    DateTimeOffset ChangedAt);
