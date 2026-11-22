namespace EFootwear.BLL.Platform.Contracts;

/// <summary>A platform administrator. Has no company; works across companies on purpose.</summary>
public sealed record PlatformAdministratorIdentity(Guid AdministratorId, string Username, string FullName);
