using System.Text.Json.Nodes;

namespace Symphony.Codex;

public enum ApprovalHandlingResult
{
    Unhandled,
    Replied,
    RequiresApproval,
    RequiresInput
}

public sealed class ApprovalHandler
{
    public const string NonInteractiveToolInputAnswer = "This is a non-interactive session. Operator input is unavailable.";

    private readonly DynamicToolDispatcher _toolDispatcher;

    public ApprovalHandler(DynamicToolDispatcher toolDispatcher)
    {
        _toolDispatcher = toolDispatcher;
    }

    public async Task<ApprovalHandlingResult> TryHandleAsync(
        JsonObject payload,
        Func<JsonObject, Task> sendAsync,
        bool autoApproveRequests,
        CancellationToken cancellationToken)
    {
        var method = payload["method"]?.GetValue<string>();
        var id = payload["id"]?.GetValue<int?>();

        if (method is null || id is null)
        {
            return ApprovalHandlingResult.Unhandled;
        }

        if (method is "item/commandExecution/requestApproval" or "item/fileChange/requestApproval")
        {
            return await ApproveOrRequireAsync(id.Value, "acceptForSession", sendAsync, autoApproveRequests);
        }

        if (method is "execCommandApproval" or "applyPatchApproval")
        {
            return await ApproveOrRequireAsync(id.Value, "approved_for_session", sendAsync, autoApproveRequests);
        }

        if (method == "item/tool/call")
        {
            var parameters = payload["params"] as JsonObject;
            var toolName = parameters?["tool"]?.GetValue<string>() ?? parameters?["name"]?.GetValue<string>();
            var arguments = parameters?["arguments"];
            var result = await _toolDispatcher.ExecuteAsync(toolName, arguments, cancellationToken);
            await sendAsync(new JsonObject { ["id"] = id.Value, ["result"] = result });
            return ApprovalHandlingResult.Replied;
        }

        if (method == "item/tool/requestUserInput")
        {
            var answers = BuildUserInputAnswers(payload["params"] as JsonObject, autoApproveRequests);
            if (answers is null)
            {
                return ApprovalHandlingResult.RequiresInput;
            }

            await sendAsync(new JsonObject { ["id"] = id.Value, ["result"] = new JsonObject { ["answers"] = answers } });
            return ApprovalHandlingResult.Replied;
        }

        return ApprovalHandlingResult.Unhandled;
    }

    public static bool NeedsInput(string? method, JsonObject payload)
    {
        if (method is null || !method.StartsWith("turn/", StringComparison.Ordinal))
        {
            return false;
        }

        if (method is "turn/input_required" or "turn/needs_input" or "turn/need_input" or "turn/request_input" or "turn/request_response" or "turn/provide_input" or "turn/approval_required")
        {
            return true;
        }

        return NeedsInputField(payload) || payload["params"] is JsonObject parameters && NeedsInputField(parameters);
    }

    private static async Task<ApprovalHandlingResult> ApproveOrRequireAsync(
        int id,
        string decision,
        Func<JsonObject, Task> sendAsync,
        bool autoApproveRequests)
    {
        if (!autoApproveRequests)
        {
            return ApprovalHandlingResult.RequiresApproval;
        }

        await sendAsync(new JsonObject { ["id"] = id, ["result"] = new JsonObject { ["decision"] = decision } });
        return ApprovalHandlingResult.Replied;
    }

    private static JsonObject? BuildUserInputAnswers(JsonObject? parameters, bool autoApproveRequests)
    {
        if (parameters?["questions"] is not JsonArray questions || questions.Count == 0)
        {
            return null;
        }

        var answers = new JsonObject();
        foreach (var questionNode in questions)
        {
            if (questionNode is not JsonObject question)
            {
                return null;
            }

            var questionId = question["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(questionId))
            {
                return null;
            }

            var answer = autoApproveRequests ? ApprovalOption(question) ?? NonInteractiveToolInputAnswer : NonInteractiveToolInputAnswer;
            answers[questionId] = new JsonObject { ["answers"] = new JsonArray(answer) };
        }

        return answers;
    }

    private static string? ApprovalOption(JsonObject question)
    {
        if (question["options"] is not JsonArray options)
        {
            return null;
        }

        var labels = options
            .OfType<JsonObject>()
            .Select(option => option["label"]?.GetValue<string>())
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .ToArray();

        return labels.FirstOrDefault(label => label == "Approve this Session")
            ?? labels.FirstOrDefault(label => label == "Approve Once")
            ?? labels.FirstOrDefault(label =>
            {
                var normalized = label!.Trim().ToLowerInvariant();
                return normalized.StartsWith("approve", StringComparison.Ordinal) || normalized.StartsWith("allow", StringComparison.Ordinal);
            });
    }

    private static bool NeedsInputField(JsonObject payload)
    {
        return payload["requiresInput"]?.GetValue<bool?>() == true
            || payload["needsInput"]?.GetValue<bool?>() == true
            || payload["input_required"]?.GetValue<bool?>() == true
            || payload["inputRequired"]?.GetValue<bool?>() == true
            || payload["type"]?.GetValue<string>() is "input_required" or "needs_input";
    }
}

