namespace Symphony.Core.Workflow;

public sealed record WorkflowDefinition(
    IReadOnlyDictionary<string, object?> Config,
    string PromptTemplate,
    string SourcePath,
    DateTimeOffset LoadedAt);
