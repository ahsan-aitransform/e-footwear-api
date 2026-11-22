namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>The read-only "Account #" shown on New Second Level before saving (docx 2.2).</summary>
public sealed record NextAccountGroupCodeResponse(string GroupCode);
