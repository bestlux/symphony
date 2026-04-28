using System.Text.Json;
using Symphony.Abstractions.Issues;

namespace Symphony.Linear;

public sealed class LinearIssueNormalizer
{
    public LinearIssue? Normalize(JsonElement issue, IReadOnlySet<string>? assigneeIds = null)
    {
        if (issue.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var assigneeId = GetString(issue, "assignee", "id");

        return new LinearIssue
        {
            Id = GetString(issue, "id"),
            Identifier = GetString(issue, "identifier"),
            Title = GetString(issue, "title"),
            Description = GetString(issue, "description"),
            Priority = GetInt(issue, "priority"),
            State = GetString(issue, "state", "name"),
            BranchName = GetString(issue, "branchName"),
            Url = GetString(issue, "url"),
            AssigneeId = assigneeId,
            Labels = ExtractLabels(issue),
            BlockedBy = ExtractBlockers(issue),
            AssignedToWorker = assigneeIds is null || (assigneeId is not null && assigneeIds.Contains(assigneeId)),
            CreatedAt = GetDateTimeOffset(issue, "createdAt"),
            UpdatedAt = GetDateTimeOffset(issue, "updatedAt")
        };
    }

    public IReadOnlyList<LinearIssue> NormalizeIssueNodes(JsonElement response, IReadOnlySet<string>? assigneeIds = null)
    {
        ThrowIfGraphQlErrors(response);

        if (!TryGetProperty(response, out var nodes, "data", "issues", "nodes") || nodes.ValueKind != JsonValueKind.Array)
        {
            throw new LinearException("Linear GraphQL response did not contain data.issues.nodes.");
        }

        var issues = new List<LinearIssue>();
        foreach (var node in nodes.EnumerateArray())
        {
            var issue = Normalize(node, assigneeIds);
            if (issue is not null)
            {
                issues.Add(issue);
            }
        }

        return issues;
    }

    public static Issue ToIssue(LinearIssue issue)
    {
        return new Issue(
            issue.Id ?? "",
            issue.Identifier ?? issue.Id ?? "issue",
            issue.Title ?? "",
            issue.Description,
            issue.Priority,
            issue.State ?? "",
            issue.BranchName,
            issue.Url,
            issue.Labels,
            issue.BlockedBy.Select(blocker => new BlockerRef(blocker.Id, blocker.Identifier, blocker.State)).ToArray(),
            issue.CreatedAt,
            issue.UpdatedAt,
            issue.AssigneeId,
            issue.AssignedToWorker);
    }

    public static void ThrowIfGraphQlErrors(JsonElement response)
    {
        if (response.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            throw new LinearException($"Linear GraphQL returned errors: {errors.GetRawText()}");
        }
    }

    private static IReadOnlyList<string> ExtractLabels(JsonElement issue)
    {
        if (!TryGetProperty(issue, out var labels, "labels", "nodes") || labels.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var names = new List<string>();
        foreach (var label in labels.EnumerateArray())
        {
            var name = GetString(label, "name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name.ToLowerInvariant());
            }
        }

        return names;
    }

    private static IReadOnlyList<LinearBlockerRef> ExtractBlockers(JsonElement issue)
    {
        if (!TryGetProperty(issue, out var relations, "inverseRelations", "nodes") || relations.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var blockers = new List<LinearBlockerRef>();
        foreach (var relation in relations.EnumerateArray())
        {
            var type = GetString(relation, "type");
            if (!string.Equals(type?.Trim(), "blocks", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (relation.TryGetProperty("issue", out var blockerIssue) && blockerIssue.ValueKind == JsonValueKind.Object)
            {
                blockers.Add(new LinearBlockerRef(
                    GetString(blockerIssue, "id"),
                    GetString(blockerIssue, "identifier"),
                    GetString(blockerIssue, "state", "name")));
            }
        }

        return blockers;
    }

    private static bool TryGetProperty(JsonElement root, out JsonElement value, params string[] path)
    {
        value = root;
        foreach (var segment in path)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
            {
                return false;
            }
        }

        return true;
    }

    private static string? GetString(JsonElement root, params string[] path)
    {
        return TryGetProperty(root, out var value, path) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? GetInt(JsonElement root, params string[] path)
    {
        return TryGetProperty(root, out var value, path) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement root, params string[] path)
    {
        var value = GetString(root, path);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }
}
