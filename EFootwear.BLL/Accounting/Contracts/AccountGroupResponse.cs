namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>A second-level group (docx 2.3: Code, Title) with how many accounts use it, so the screen can tell whether it may be deleted.</summary>
public sealed record AccountGroupResponse(string GroupCode, string MainTypeCode, string Name, int AccountCount, long RowVersion);
