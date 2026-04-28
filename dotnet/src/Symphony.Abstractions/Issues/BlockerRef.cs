namespace Symphony.Abstractions.Issues;

public sealed record BlockerRef(
    string? Id,
    string? Identifier,
    string? State);
