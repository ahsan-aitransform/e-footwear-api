namespace EFootwear.BLL.Core.Contracts;

/// <summary>A store's settings: its own overrides (null field = inherits) and the effective values it really uses.</summary>
public sealed record StoreSettingsDocumentResponse<TValues>(TValues Overrides, TValues Effective, long RowVersion, bool IsStored);
