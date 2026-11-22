namespace EFootwear.BLL.Parties.Contracts;

/// <summary>Moves a staff member to another store of the same company (docx 3.1 improvement "Transfer to Store"; Part 16.1).</summary>
public sealed record TransferStaffRequest(Guid ToStoreId, DateOnly EffectiveOn);
