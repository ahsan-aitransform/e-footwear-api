namespace EFootwear.BLL.Staff.Contracts;

/// <summary>Moves a staff member to another store of the same company (Part 16.1).</summary>
public sealed record TransferStaffRequest(Guid ToStoreId, DateOnly EffectiveOn);
