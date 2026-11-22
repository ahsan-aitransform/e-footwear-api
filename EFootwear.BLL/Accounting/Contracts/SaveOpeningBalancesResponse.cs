namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>What the save did: lines posted or replaced, lines left as they were, and balances cleared back to zero.</summary>
public sealed record SaveOpeningBalancesResponse(int PostedCount, int UnchangedCount, int ClearedCount);
