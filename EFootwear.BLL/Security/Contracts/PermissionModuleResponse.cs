namespace EFootwear.BLL.Security.Contracts;

public sealed record PermissionModuleResponse(string ModuleCode, string DisplayName,
                                              IReadOnlyList<PermissionScreenResponse> Screens);
