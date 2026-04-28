using System.Text.Json.Nodes;

namespace Symphony.Codex;

public static class CodexEventMapper
{
    public static CodexRuntimeUpdate Map(
        string eventName,
        JsonObject? payload,
        string? raw,
        int? processId,
        string? workerHost,
        string? sessionId = null,
        string? threadId = null,
        string? turnId = null)
    {
        var mappedEventName = MapEventName(eventName, payload);
        var usage = FindObject(payload, "usage");
        var rateLimits = FindNode(payload, "rateLimits", "rate_limits");

        return new CodexRuntimeUpdate(
            mappedEventName,
            DateTimeOffset.UtcNow,
            sessionId,
            threadId,
            turnId,
            processId?.ToString(),
            workerHost,
            raw ?? payload?.ToJsonString(JsonDefaults.Options),
            Token(usage, "input_tokens", "inputTokens", "input"),
            Token(usage, "output_tokens", "outputTokens", "output"),
            Token(usage, "total_tokens", "totalTokens", "total"),
            rateLimits,
            payload);
    }

    private static string MapEventName(string eventName, JsonObject? payload)
    {
        if (!string.Equals(eventName, "notification", StringComparison.Ordinal))
        {
            return eventName;
        }

        if (payload?["method"] is JsonValue methodValue
            && methodValue.TryGetValue<string>(out var method)
            && !string.IsNullOrWhiteSpace(method))
        {
            return method;
        }

        return eventName;
    }

    private static JsonObject? FindObject(JsonObject? payload, string propertyName)
    {
        if (payload is null)
        {
            return null;
        }

        if (payload[propertyName] is JsonObject direct)
        {
            return direct;
        }

        if (payload["params"] is JsonObject parameters && parameters[propertyName] is JsonObject nested)
        {
            return nested;
        }

        return null;
    }

    private static JsonNode? FindNode(JsonObject? payload, params string[] propertyNames)
    {
        if (payload is null)
        {
            return null;
        }

        foreach (var propertyName in propertyNames)
        {
            if (payload[propertyName] is { } direct)
            {
                return direct;
            }

            if (payload["params"] is JsonObject parameters && parameters[propertyName] is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static long Token(JsonObject? usage, params string[] names)
    {
        if (usage is null)
        {
            return 0;
        }

        foreach (var name in names)
        {
            if (usage[name] is JsonValue value && value.TryGetValue<long>(out var count))
            {
                return count;
            }
        }

        return 0;
    }
}

