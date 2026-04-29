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
    bool? AssignedToWorker = null,
    IReadOnlyList<IssueComment>? Comments = null,
    IReadOnlyList<IssueLink>? Links = null);

public sealed record IssueComment(
    string Id,
    string? Body,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record IssueLink(
    string Id,
    string? Title,
    string? Url,
    string? SourceType);
