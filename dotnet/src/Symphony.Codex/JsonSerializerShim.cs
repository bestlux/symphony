using System.Text.Json;
using System.Text.Json.Nodes;

namespace Symphony.Codex;

internal static class JsonSerializerShim
{
    public static JsonNode? ToNode(object? value)
    {
        if (value is null)
        {
            return null;
        }

        return JsonSerializer.SerializeToNode(value, JsonDefaults.Options);
    }
}

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
}

