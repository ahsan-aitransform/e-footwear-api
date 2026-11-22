namespace EFootwear.Domain.Core.Settings;

/// <summary>The colour themes a person can choose (user preference). The codes match the frontend theme store.</summary>
public static class ThemeCodes
{
    public const string Light = "light";
    public const string Dark = "dark";
    public const string FollowSystem = "system";

    public static readonly IReadOnlyList<string> All = [Light, Dark, FollowSystem];
}
