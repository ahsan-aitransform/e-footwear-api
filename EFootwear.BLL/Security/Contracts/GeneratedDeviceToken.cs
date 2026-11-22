namespace EFootwear.BLL.Security.Contracts;

/// <summary>A fresh device token and its SHA-256 hash. The token is handed out once; only the hash is stored.</summary>
public sealed record GeneratedDeviceToken(string Token, string TokenHash);
