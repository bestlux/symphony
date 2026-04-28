using Scriban;
using Scriban.Runtime;
using Symphony.Abstractions.Issues;

namespace Symphony.Core.Prompts;

public sealed class PromptRenderer
{
    public const string DefaultPromptTemplate = """
        You are working on a Linear issue.

        Identifier: {{ issue.identifier }}
        Title: {{ issue.title }}

        Body:
        {{ if issue.description }}
        {{ issue.description }}
        {{ else }}
        No description provided.
        {{ end }}
        """;

    public string Render(string? promptTemplate, Issue issue, int? attempt = null)
    {
        var source = string.IsNullOrWhiteSpace(promptTemplate) ? DefaultPromptTemplate : promptTemplate;
        var template = source.Contains("{%", StringComparison.Ordinal)
            ? Template.ParseLiquid(source)
            : Template.Parse(source);

        if (template.HasErrors)
        {
            throw new PromptRenderException("Template parse error: " + string.Join("; ", template.Messages));
        }

        var script = new ScriptObject
        {
            ["issue"] = IssueToTemplateObject(issue),
            ["attempt"] = attempt
        };

        var context = new TemplateContext
        {
            StrictVariables = true,
            EnableRelaxedMemberAccess = false
        };
        context.PushGlobal(script);

        try
        {
            return template.Render(context);
        }
        catch (Exception ex)
        {
            throw new PromptRenderException($"Template render error: {ex.Message}", ex);
        }
    }

    private static Dictionary<string, object?> IssueToTemplateObject(Issue issue)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = issue.Id,
            ["identifier"] = issue.Identifier,
            ["title"] = issue.Title,
            ["description"] = issue.Description,
            ["priority"] = issue.Priority,
            ["state"] = issue.State,
            ["branch_name"] = issue.BranchName,
            ["url"] = issue.Url,
            ["labels"] = issue.Labels,
            ["blocked_by"] = issue.BlockedBy.Select(blocker => new Dictionary<string, object?>
            {
                ["id"] = blocker.Id,
                ["identifier"] = blocker.Identifier,
                ["state"] = blocker.State
            }).ToArray(),
            ["created_at"] = issue.CreatedAt,
            ["updated_at"] = issue.UpdatedAt,
            ["assignee_id"] = issue.AssigneeId,
            ["assigned_to_worker"] = issue.AssignedToWorker
        };
    }
}

public sealed class PromptRenderException : InvalidOperationException
{
    public PromptRenderException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
