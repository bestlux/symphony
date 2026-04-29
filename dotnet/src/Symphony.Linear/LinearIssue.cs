namespace Symphony.Linear;

public sealed record LinearIssue
{
    public string? Id { get; init; }
    public string? Identifier { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public int? Priority { get; init; }
    public string? State { get; init; }
    public string? BranchName { get; init; }
    public string? Url { get; init; }
    public string? AssigneeId { get; init; }
    public IReadOnlyList<string> Labels { get; init; } = [];
    public IReadOnlyList<LinearBlockerRef> BlockedBy { get; init; } = [];
    public IReadOnlyList<LinearComment> Comments { get; init; } = [];
    public IReadOnlyList<LinearAttachment> Attachments { get; init; } = [];
    public bool AssignedToWorker { get; init; } = true;
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed record LinearBlockerRef(string? Id, string? Identifier, string? State);

public sealed record LinearComment(string? Id, string? Body, DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt);

public sealed record LinearAttachment(string? Id, string? Title, string? Url, string? SourceType);

