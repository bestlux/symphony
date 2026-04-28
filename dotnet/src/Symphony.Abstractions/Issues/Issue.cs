namespace Symphony.Abstractions.Issues;

public sealed record Issue(
    string Id,
    string Identifier,
    string Title,
    string? Description,
    int? Priority,
    string State,
    string? BranchName,
    string? Url,
    IReadOnlyList<string> Labels,
    IReadOnlyList<BlockerRef> BlockedBy,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    string? AssigneeId,
    bool? AssignedToWorker = null);
