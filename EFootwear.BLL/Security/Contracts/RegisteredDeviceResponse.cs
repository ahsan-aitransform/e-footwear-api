namespace EFootwear.BLL.Security.Contracts;

/// <summary>A new or re-keyed device together with its token. The token is shown ONCE; only its hash is stored.</summary>
public sealed record RegisteredDeviceResponse(DeviceResponse Device, string DeviceToken);
