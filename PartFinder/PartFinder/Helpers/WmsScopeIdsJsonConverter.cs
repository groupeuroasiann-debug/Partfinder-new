using System.Text.Json;
using System.Text.Json.Serialization;

namespace PartFinder.Helpers;

/// <summary>
/// WMS session profile returns <c>"all"</c> or a string array for scope fields.
/// </summary>
public sealed class WmsScopeIdsJsonConverter : JsonConverter<string[]>
{
    public override string[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String when string.Equals(reader.GetString(), "all", StringComparison.OrdinalIgnoreCase)
                => [],
            JsonTokenType.String => [reader.GetString() ?? string.Empty],
            JsonTokenType.StartArray => JsonSerializer.Deserialize<string[]>(ref reader, options) ?? [],
            JsonTokenType.Null => [],
            _ => throw new JsonException(
                $"Expected \"all\", array, or null for WMS scope ids, got {reader.TokenType}."),
        };
    }

    public override void Write(Utf8JsonWriter writer, string[]? value, JsonSerializerOptions options)
    {
        if (value is null || value.Length == 0)
        {
            writer.WriteStartArray();
            writer.WriteEndArray();
            return;
        }

        JsonSerializer.Serialize(writer, value, options);
    }
}