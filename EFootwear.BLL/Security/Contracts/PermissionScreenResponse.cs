namespace EFootwear.BLL.Security.Contracts;

public sealed record PermissionScreenResponse(string ScreenCode, string DisplayName, string? SubGroupCode,
                                              IReadOnlyList<PermissionActionResponse> Actions);
