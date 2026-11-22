namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>The read-only "Account #" shown on the GL account screen before saving (docx 2.1: next code in the group).</summary>
public sealed record NextAccountCodeResponse(string AccountCode);
