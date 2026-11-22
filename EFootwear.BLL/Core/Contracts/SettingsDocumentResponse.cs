namespace EFootwear.BLL.Core.Contracts;

/// <summary>
/// One settings section as the screen needs it. <paramref name="RowVersion"/> goes back in the next save as the expected version, so a
/// change made by someone else in between is refused (409). <paramref name="IsStored"/> is false while nothing was ever saved: the
/// screen then shows "not configured".
/// </summary>
public sealed record SettingsDocumentResponse<TValues>(TValues Values, long RowVersion, bool IsStored);
