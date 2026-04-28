using System.Text.Json.Nodes;
using Symphony.Abstractions.Issues;
using Symphony.Abstractions.Tracking;

namespace Symphony.Linear;

public sealed class LinearTrackerClient : ITrackerClient
{
    private readonly LinearClient _client;
    private readonly Func<LinearOptions> _optionsFactory;
    private readonly LinearIssueNormalizer _normalizer;

    public LinearTrackerClient(LinearClient client, LinearOptions options)
        : this(client, () => options, new LinearIssueNormalizer())
    {
    }

    public LinearTrackerClient(LinearClient client, ILinearOptionsProvider optionsProvider)
        : this(client, optionsProvider.GetLinearOptions, new LinearIssueNormalizer())
    {
    }

    public LinearTrackerClient(LinearClient client, Func<LinearOptions> optionsFactory, LinearIssueNormalizer normalizer)
    {
        _client = client;
        _optionsFactory = optionsFactory;
        _normalizer = normalizer;
    }

    public async Task<IReadOnlyList<Issue>> FetchCandidateIssuesAsync(CancellationToken cancellationToken = default)
    {
        var options = RequireOptions(projectRequired: true);
        var assigneeFilter = await ResolveAssigneeFilterAsync(options, cancellationToken).ConfigureAwait(false);
        var issues = await FetchByStatesAsync(options.ProjectSlug!, options.ActiveStates, assigneeFilter, cancellationToken).ConfigureAwait(false);
        return issues.Select(LinearIssueNormalizer.ToIssue).ToArray();
    }

    public async Task<IReadOnlyList<Issue>> FetchIssuesByStatesAsync(IReadOnlyList<string> states, CancellationToken cancellationToken = default)
    {
        var normalizedStates = states.Select(static state => state.Trim()).Where(static state => state.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        if (normalizedStates.Length == 0)
        {
            return [];
        }

        var options = RequireOptions(projectRequired: true);
        var issues = await FetchByStatesAsync(options.ProjectSlug!, normalizedStates, assigneeIds: null, cancellationToken).ConfigureAwait(false);
        return issues.Select(LinearIssueNormalizer.ToIssue).ToArray();
    }

    public async Task<IReadOnlyList<Issue>> FetchIssueStatesByIdsAsync(IReadOnlyList<string> issueIds, CancellationToken cancellationToken = default)
    {
        var ids = issueIds.Select(static id => id.Trim()).Where(static id => id.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        RequireOptions(projectRequired: false);
        var assigneeFilter = await ResolveAssigneeFilterAsync(_optionsFactory().ResolveEnvironment(), cancellationToken).ConfigureAwait(false);
        var order = ids.Select((id, index) => (id, index)).ToDictionary(static item => item.id, static item => item.index, StringComparer.Ordinal);
        var issues = new List<LinearIssue>();

        foreach (var batch in ids.Chunk(LinearQueries.IssuePageSize))
        {
            var variables = new JsonObject
            {
                ["ids"] = new JsonArray(batch.Select(static id => JsonValue.Create(id)).ToArray<JsonNode?>()),
                ["first"] = batch.Length,
                ["relationFirst"] = LinearQueries.IssuePageSize
            };

            using var response = await _client.GraphQlAsync(LinearQueries.IssuesByIds, variables, cancellationToken).ConfigureAwait(false);
            issues.AddRange(_normalizer.NormalizeIssueNodes(response.RootElement, assigneeFilter));
        }

        return issues
            .OrderBy(issue => issue.Id is not null && order.TryGetValue(issue.Id, out var index) ? index : order.Count)
            .Select(LinearIssueNormalizer.ToIssue)
            .ToArray();
    }

    public Task<System.Text.Json.JsonDocument> GraphQlAsync(string query, JsonObject variables, CancellationToken cancellationToken = default)
    {
        return _client.GraphQlAsync(query, variables, cancellationToken);
    }

    public async Task ValidateWorkflowStatesAsync(
        IReadOnlyList<string> requiredStates,
        IReadOnlyList<IReadOnlyList<string>> requiredAlternatives,
        CancellationToken cancellationToken = default)
    {
        var options = RequireOptions(projectRequired: true);
        var variables = new JsonObject
        {
            ["projectSlug"] = options.ProjectSlug!,
            ["first"] = LinearQueries.IssuePageSize
        };

        using var response = await _client.GraphQlAsync(LinearQueries.ProjectWorkflowStates, variables, cancellationToken).ConfigureAwait(false);
        LinearIssueNormalizer.ThrowIfGraphQlErrors(response.RootElement);

        var stateNames = WorkflowStateNames(response.RootElement);
        var missing = requiredStates
            .Where(required => !stateNames.Contains(required))
            .ToList();

        foreach (var alternatives in requiredAlternatives)
        {
            if (!alternatives.Any(stateNames.Contains))
            {
                missing.Add(string.Join(" or ", alternatives));
            }
        }

        if (missing.Count > 0)
        {
            throw new LinearException($"Linear workflow for project '{options.ProjectSlug}' is missing required state(s): {string.Join(", ", missing)}.");
        }
    }

    public async Task CreateCommentAsync(string issueId, string body, CancellationToken cancellationToken = default)
    {
        var variables = new JsonObject
        {
            ["issueId"] = issueId,
            ["body"] = body
        };

        using var response = await _client.GraphQlAsync(LinearQueries.CreateComment, variables, cancellationToken).ConfigureAwait(false);
        LinearIssueNormalizer.ThrowIfGraphQlErrors(response.RootElement);

        if (!TryGetBool(response.RootElement, out var success, "data", "commentCreate", "success") || !success)
        {
            throw new LinearException("Linear comment creation failed.");
        }
    }

    public async Task UpdateIssueStateAsync(string issueId, string stateName, CancellationToken cancellationToken = default)
    {
        var stateId = await ResolveStateIdAsync(issueId, stateName, cancellationToken).ConfigureAwait(false);
        var variables = new JsonObject
        {
            ["issueId"] = issueId,
            ["stateId"] = stateId
        };

        using var response = await _client.GraphQlAsync(LinearQueries.UpdateState, variables, cancellationToken).ConfigureAwait(false);
        LinearIssueNormalizer.ThrowIfGraphQlErrors(response.RootElement);

        if (!TryGetBool(response.RootElement, out var success, "data", "issueUpdate", "success") || !success)
        {
            throw new LinearException("Linear issue state update failed.");
        }
    }

    public async Task<string> ResolveStateIdAsync(string issueId, string stateName, CancellationToken cancellationToken = default)
    {
        var variables = new JsonObject
        {
            ["issueId"] = issueId,
            ["stateName"] = stateName
        };

        using var response = await _client.GraphQlAsync(LinearQueries.ResolveStateId, variables, cancellationToken).ConfigureAwait(false);
        LinearIssueNormalizer.ThrowIfGraphQlErrors(response.RootElement);

        if (TryGetString(response.RootElement, out var stateId, "data", "issue", "team", "states", "nodes", "0", "id"))
        {
            return stateId;
        }

        throw new LinearException($"Linear state '{stateName}' was not found for issue '{issueId}'.");
    }

    private async Task<IReadOnlyList<LinearIssue>> FetchByStatesAsync(
        string projectSlug,
        IReadOnlyList<string> states,
        IReadOnlySet<string>? assigneeIds,
        CancellationToken cancellationToken)
    {
        var allIssues = new List<LinearIssue>();
        string? cursor = null;

        while (true)
        {
            var variables = new JsonObject
            {
                ["projectSlug"] = projectSlug,
                ["stateNames"] = new JsonArray(states.Select(static state => JsonValue.Create(state)).ToArray<JsonNode?>()),
                ["first"] = LinearQueries.IssuePageSize,
                ["relationFirst"] = LinearQueries.IssuePageSize,
                ["after"] = cursor
            };

            using var response = await _client.GraphQlAsync(LinearQueries.CandidateIssues, variables, cancellationToken).ConfigureAwait(false);
            allIssues.AddRange(_normalizer.NormalizeIssueNodes(response.RootElement, assigneeIds));

            var hasNextPage = TryGetBool(response.RootElement, out var next, "data", "issues", "pageInfo", "hasNextPage") && next;
            if (!hasNextPage)
            {
                return allIssues;
            }

            if (!TryGetString(response.RootElement, out cursor, "data", "issues", "pageInfo", "endCursor") || string.IsNullOrWhiteSpace(cursor))
            {
                throw new LinearException("Linear response indicated another page but did not include pageInfo.endCursor.");
            }
        }
    }

    private static HashSet<string> WorkflowStateNames(System.Text.Json.JsonElement root)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetElement(root, out var projects, "data", "projects", "nodes"))
        {
            return names;
        }

        foreach (var project in projects.EnumerateArray())
        {
            if (!TryGetElement(project, out var teams, "teams", "nodes"))
            {
                continue;
            }

            foreach (var team in teams.EnumerateArray())
            {
                if (!TryGetElement(team, out var states, "states", "nodes"))
                {
                    continue;
                }

                foreach (var state in states.EnumerateArray())
                {
                    if (TryGetString(state, out var name, "name") && !string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name);
                    }
                }
            }
        }

        return names;
    }

    private async Task<IReadOnlySet<string>?> ResolveAssigneeFilterAsync(LinearOptions options, CancellationToken cancellationToken)
    {
        var assignee = options.Assignee?.Trim();
        if (string.IsNullOrWhiteSpace(assignee))
        {
            return null;
        }

        if (!string.Equals(assignee, "me", StringComparison.OrdinalIgnoreCase))
        {
            return new HashSet<string>([assignee], StringComparer.Ordinal);
        }

        using var response = await _client.GraphQlAsync(LinearQueries.Viewer, new JsonObject(), cancellationToken).ConfigureAwait(false);
        LinearIssueNormalizer.ThrowIfGraphQlErrors(response.RootElement);

        return TryGetString(response.RootElement, out var viewerId, "data", "viewer", "id")
            ? new HashSet<string>([viewerId], StringComparer.Ordinal)
            : throw new LinearException("Linear viewer identity could not be resolved for assignee 'me'.");
    }

    private LinearOptions RequireOptions(bool projectRequired)
    {
        var options = _optionsFactory().ResolveEnvironment();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new LinearException("Linear API token is missing. Set tracker.api_key or LINEAR_API_KEY.");
        }

        if (projectRequired && string.IsNullOrWhiteSpace(options.ProjectSlug))
        {
            throw new LinearException("Linear project slug is missing. Set tracker.project_slug.");
        }

        return options;
    }

    private static bool TryGetString(System.Text.Json.JsonElement root, out string value, params string[] path)
    {
        if (TryGetElement(root, out var element, path) && element.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            value = element.GetString()!;
            return true;
        }

        value = "";
        return false;
    }

    private static bool TryGetBool(System.Text.Json.JsonElement root, out bool value, params string[] path)
    {
        if (TryGetElement(root, out var element, path) && element.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
        {
            value = element.GetBoolean();
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryGetElement(System.Text.Json.JsonElement root, out System.Text.Json.JsonElement value, params string[] path)
    {
        value = root;
        foreach (var segment in path)
        {
            if (value.ValueKind == System.Text.Json.JsonValueKind.Array && int.TryParse(segment, out var index))
            {
                if (index < 0 || index >= value.GetArrayLength())
                {
                    return false;
                }

                value = value[index];
                continue;
            }

            if (value.ValueKind != System.Text.Json.JsonValueKind.Object || !value.TryGetProperty(segment, out value))
            {
                return false;
            }
        }

        return true;
    }
}
