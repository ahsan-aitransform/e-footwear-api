namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>View Second Level, Edit (docx 2.3): only the title of a group can change; the code is fixed once accounts use it.</summary>
public sealed record UpdateAccountGroupRequest(string Name, long RowVersion);
