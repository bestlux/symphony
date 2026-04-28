using System.Text.Json.Nodes;

namespace Symphony.Codex;

public static class CodexProtocol
{
    public const int InitializeId = 1;
    public const int ThreadStartId = 2;
    public const int TurnStartId = 3;

    public static JsonObject Initialize() => new()
    {
        ["method"] = "initialize",
        ["id"] = InitializeId,
        ["params"] = new JsonObject
        {
            ["capabilities"] = new JsonObject { ["experimentalApi"] = true },
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "symphony-orchestrator",
                ["title"] = "Symphony Orchestrator",
                ["version"] = "0.1.0"
            }
        }
    };

    public static JsonObject Initialized() => new()
    {
        ["method"] = "initialized",
        ["params"] = new JsonObject()
    };

    public static JsonObject ThreadStart(string cwd, CodexOptions options) => new()
    {
        ["method"] = "thread/start",
        ["id"] = ThreadStartId,
        ["params"] = new JsonObject
        {
            ["approvalPolicy"] = JsonSerializerShim.ToNode(options.ApprovalPolicy),
            ["sandbox"] = options.ThreadSandbox,
            ["cwd"] = cwd,
            ["dynamicTools"] = DynamicToolDispatcher.ToolSpecs()
        }
    };

    public static JsonObject TurnStart(string threadId, string prompt, string cwd, string title, CodexOptions options) => new()
    {
        ["method"] = "turn/start",
        ["id"] = TurnStartId,
        ["params"] = new JsonObject
        {
            ["threadId"] = threadId,
            ["input"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = prompt }),
            ["cwd"] = cwd,
            ["title"] = title,
            ["approvalPolicy"] = JsonSerializerShim.ToNode(options.ApprovalPolicy),
            ["sandboxPolicy"] = JsonSerializerShim.ToNode(options.TurnSandboxPolicy)
        }
    };
}
