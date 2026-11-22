namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>A main type of the chart of accounts for the "Main Type" dropdown (docx 2.1).</summary>
public sealed record AccountMainTypeResponse(string Code, string DigitPrefix, string DisplayName);
