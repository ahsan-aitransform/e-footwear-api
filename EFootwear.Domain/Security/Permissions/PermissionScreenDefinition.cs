namespace EFootwear.Domain.Security.Permissions;

/// <summary>
/// A screen (or a group of restricted operations) inside a module. <paramref name="SubGroupCode"/> is set for the
/// two sub-groups of the original menu: ACCOUNT_REPORTS inside Reports and GL_ACCOUNT inside Settings.
/// </summary>
public sealed record PermissionScreenDefinition(string ScreenCode, string DisplayName, string? SubGroupCode,
                                                IReadOnlyList<PermissionActionDefinition> Actions);
