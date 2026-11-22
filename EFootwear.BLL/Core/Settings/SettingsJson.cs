using System.Text.Json;
using System.Text.Json.Serialization;

namespace EFootwear.BLL.Core.Settings;

/// <summary>
/// How a settings section is written to and read from the JSON column: camelCase names, and a value that is not set is left out
/// (an absent key means "not configured").
/// </summary>
public static class SettingsJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize<TValues>(TValues values) => JsonSerializer.Serialize(values, SerializerOptions);

    public static TValues Deserialize<TValues>(string valuesJson) =>
        JsonSerializer.Deserialize<TValues>(valuesJson, SerializerOptions)
        ?? throw new InvalidOperationException("A stored settings section could not be read.");
}
