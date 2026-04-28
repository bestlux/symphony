using System.Text.Json;
using System.Text.Json.Nodes;
using Symphony.Abstractions.Issues;

namespace Symphony.Abstractions.Tracking;

public interface ITrackerClient
{
    Task<IReadOnlyList<Issue>> FetchCandidateIssuesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Issue>> FetchIssuesByStatesAsync(
        IReadOnlyList<string> states,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Issue>> FetchIssueStatesByIdsAsync(
        IReadOnlyList<string> issueIds,
        CancellationToken cancellationToken);

    Task<JsonDocument> GraphQlAsync(
        string query,
        JsonObject variables,
        CancellationToken cancellationToken);

    Task CreateCommentAsync(string issueId, string body, CancellationToken cancellationToken);

    Task UpdateIssueStateAsync(string issueId, string stateName, CancellationToken cancellationToken);
}
