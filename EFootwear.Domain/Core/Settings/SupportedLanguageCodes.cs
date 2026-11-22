namespace EFootwear.Domain.Core.Settings;

/// <summary>The interface and print languages (Part 7): English and Urdu. The codes match the frontend (lib/constants/languageCodes).</summary>
public static class SupportedLanguageCodes
{
    public const string English = "en";
    public const string Urdu = "ur";

    public static readonly IReadOnlyList<string> All = [English, Urdu];
}
