namespace EFootwear.BLL.Core.Contracts;

/// <summary>Saves a settings section. <paramref name="ExpectedRowVersion"/> is the version the screen loaded (null = do not check).</summary>
public sealed record SaveSettingsRequest<TValues>(TValues Values, long? ExpectedRowVersion);
