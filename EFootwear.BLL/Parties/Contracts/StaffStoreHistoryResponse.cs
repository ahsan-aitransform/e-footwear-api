namespace EFootwear.BLL.Parties.Contracts;

/// <summary>One move of a staff member between stores: previous store, new store, effective date and who recorded it (Part 16.1).</summary>
public sealed record StaffStoreHistoryResponse(
    Guid Id,
    Guid? FromStoreId,
    string? FromStoreName,
    Guid ToStoreId,
    string ToStoreName,
    DateOnly EffectiveOn,
    Guid? ChangedByUserId,
    DateTimeOffset ChangedAt);
