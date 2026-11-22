namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>Edit an account (docx 2.1: Edit loads the account; only the title and the active flag change, never the code or the group).</summary>
public sealed record UpdateAccountRequest(string Name, bool IsActive, long RowVersion);
