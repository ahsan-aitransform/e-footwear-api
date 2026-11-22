using System.Text.Json.Nodes;

namespace EFootwear.DAL.Parties;

/// <summary>
/// Reads and writes the party fields that the schema has no column for (Urdu father name, Urdu address, notes), which live in
/// party.parties.attributes (jsonb). Unknown keys already in the JSON are kept, so another feature can add its own keys.
/// </summary>
internal static class PartyAttributesJson
{
    public const string FatherNameUrduKey = "fatherNameUrdu";
    public const string AddressUrduKey = "addressUrdu";
    public const string NotesKey = "notes";

    public static (string? FatherNameUrdu, string? AddressUrdu, string? Notes) Read(string? attributesJson)
    {
        var attributes = Parse(attributesJson);
        return (ReadText(attributes, FatherNameUrduKey), ReadText(attributes, AddressUrduKey), ReadText(attributes, NotesKey));
    }

    public static string Merge(string? existingAttributesJson, string? fatherNameUrdu, string? addressUrdu, string? notes)
    {
        var attributes = Parse(existingAttributesJson);
        SetOrRemove(attributes, FatherNameUrduKey, fatherNameUrdu);
        SetOrRemove(attributes, AddressUrduKey, addressUrdu);
        SetOrRemove(attributes, NotesKey, notes);
        return attributes.ToJsonString();
    }

    private static JsonObject Parse(string? attributesJson)
    {
        if (string.IsNullOrWhiteSpace(attributesJson)) return [];
        return JsonNode.Parse(attributesJson) as JsonObject ?? [];
    }

    private static string? ReadText(JsonObject attributes, string key) =>
        attributes.TryGetPropertyValue(key, out var node) ? node?.GetValue<string>() : null;

    private static void SetOrRemove(JsonObject attributes, string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) attributes.Remove(key);
        else attributes[key] = value;
    }
}
