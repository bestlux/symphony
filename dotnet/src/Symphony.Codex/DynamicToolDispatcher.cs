using System.Text.Json;
using System.Text.Json.Nodes;
using Symphony.Abstractions.Tracking;

namespace Symphony.Codex;

public sealed class DynamicToolDispatcher
{
    public const string LinearGraphQlToolName = "linear_graphql";

    private readonly ITrackerClient? _trackerClient;

    public DynamicToolDispatcher(ITrackerClient? trackerClient = null)
    {
        _trackerClient = trackerClient;
    }

    public static JsonArray ToolSpecs() => new()
    {
        new JsonObject
        {
            ["name"] = LinearGraphQlToolName,
            ["description"] = "Execute a raw GraphQL query or mutation against Linear using Symphony's configured auth.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JsonArray("query"),
                ["properties"] = new JsonObject
                {
                    ["query"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "GraphQL query or mutation document to execute against Linear."
                    },
                    ["variables"] = new JsonObject
                    {
                        ["type"] = new JsonArray("object", "null"),
                        ["description"] = "Optional GraphQL variables object.",
                        ["additionalProperties"] = true
                    }
                }
            }
        }
    };

    public async Task<JsonObject> ExecuteAsync(string? tool, JsonNode? arguments, CancellationToken cancellationToken)
    {
        if (tool != LinearGraphQlToolName)
        {
            return Failure(new JsonObject
            {
                ["error"] = new JsonObject
                {
                    ["message"] = $"Unsupported dynamic tool: {tool ?? "<null>"}.",
                    ["supportedTools"] = new JsonArray(LinearGraphQlToolName)
                }
            });
        }

        if (_trackerClient is null)
        {
            return Failure(new JsonObject
            {
                ["error"] = new JsonObject { ["message"] = "No tracker client is registered for `linear_graphql`." }
            });
        }

        var normalized = NormalizeLinearGraphQlArguments(arguments);
        if (normalized.Error is not null)
        {
            return Failure(normalized.Error);
        }

        try
        {
            using var response = await _trackerClient.GraphQlAsync(normalized.Query!, normalized.Variables!, cancellationToken);
            var clone = JsonNode.Parse(response.RootElement.GetRawText()) ?? new JsonObject();
            var success = clone["errors"] is not JsonArray { Count: > 0 };
            return ToolResponse(success, clone.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            return Failure(new JsonObject
            {
                ["error"] = new JsonObject
                {
                    ["message"] = "Linear GraphQL tool execution failed.",
                    ["reason"] = ex.Message
                }
            });
        }
    }

    private static (string? Query, JsonObject? Variables, JsonObject? Error) NormalizeLinearGraphQlArguments(JsonNode? arguments)
    {
        if (arguments is JsonValue value && value.TryGetValue<string>(out var rawQuery))
        {
            rawQuery = rawQuery.Trim();
            return rawQuery.Length == 0
                ? (null, null, MissingQuery())
                : (rawQuery, new JsonObject(), null);
        }

        if (arguments is not JsonObject obj)
        {
            return (null, null, new JsonObject
            {
                ["error"] = new JsonObject { ["message"] = "`linear_graphql` expects either a GraphQL query string or an object with `query` and optional `variables`." }
            });
        }

        var query = obj["query"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            return (null, null, MissingQuery());
        }

        var variablesNode = obj["variables"];
        if (variablesNode is null)
        {
            return (query, new JsonObject(), null);
        }

        if (variablesNode is not JsonObject variables)
        {
            return (null, null, new JsonObject
            {
                ["error"] = new JsonObject { ["message"] = "`linear_graphql.variables` must be a JSON object when provided." }
            });
        }

        return (query, (JsonObject)variables.DeepClone(), null);
    }

    private static JsonObject MissingQuery() => new()
    {
        ["error"] = new JsonObject { ["message"] = "`linear_graphql` requires a non-empty `query` string." }
    };

    private static JsonObject Failure(JsonObject payload) => ToolResponse(false, payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

    private static JsonObject ToolResponse(bool success, string output) => new()
    {
        ["success"] = success,
        ["output"] = output,
        ["contentItems"] = new JsonArray(new JsonObject
        {
            ["type"] = "inputText",
            ["text"] = output
        })
    };
}
