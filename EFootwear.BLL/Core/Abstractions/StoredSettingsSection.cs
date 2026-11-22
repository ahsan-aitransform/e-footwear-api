namespace EFootwear.BLL.Core.Abstractions;

/// <summary>A settings section as stored: its JSON text and row version.</summary>
public sealed record StoredSettingsSection(string ValuesJson, long RowVersion);
