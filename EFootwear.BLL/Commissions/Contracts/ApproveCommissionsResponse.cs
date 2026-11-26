namespace EFootwear.BLL.Commissions.Contracts;

/// <summary>How many pending entries the approval turned into approved entries. Zero is a normal answer: nothing was pending, or manager commission is still below the store minimum.</summary>
public sealed record ApproveCommissionsResponse(int ApprovedEntryCount);
