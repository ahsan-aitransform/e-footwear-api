namespace EFootwear.BLL.Platform.Contracts;

/// <summary>AccessToken looks like "admin.{secret}". Platform tokens never carry a company id.</summary>
public sealed record PlatformLoginResponse(string AccessToken);
