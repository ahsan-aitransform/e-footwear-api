using System.Text;

namespace EFootwear.DAL.Common;

/// <summary>
/// Builds the pattern of a "contains" search for <c>EF.Functions.ILike(column, pattern, EscapeCharacter)</c>. A person who types
/// 50% or a_b must find exactly that text, so the LIKE wildcards inside the typed text are escaped.
/// </summary>
public static class LikePattern
{
    public const string EscapeCharacter = "\\";

    public static string Contains(string searchText) => "%" + Escape(searchText) + "%";

    /// <summary>The text must be at the start of the value ("Start" match mode).</summary>
    public static string StartsWith(string searchText) => Escape(searchText) + "%";

    /// <summary>The text must be at the end of the value ("End" match mode).</summary>
    public static string EndsWith(string searchText) => "%" + Escape(searchText);

    private static string Escape(string searchText)
    {
        var builder = new StringBuilder();
        foreach (var character in searchText)
        {
            if (character is '\\' or '%' or '_') builder.Append('\\');
            builder.Append(character);
        }
        return builder.ToString();
    }
}
