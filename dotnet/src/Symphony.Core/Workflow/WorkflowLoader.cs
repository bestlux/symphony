using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Symphony.Core.Workflow;

public sealed class WorkflowLoader
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder().Build();

    public WorkflowDefinition Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new WorkflowLoadException("Workflow path must not be blank.");
        }

        var fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            throw new WorkflowLoadException($"Missing WORKFLOW.md at {fullPath}.", "missing_workflow_file");
        }

        string content;
        try
        {
            content = File.ReadAllText(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new WorkflowLoadException($"Missing WORKFLOW.md at {fullPath}: {ex.Message}", "missing_workflow_file", ex);
        }

        var (frontMatter, body) = SplitFrontMatter(content);
        var config = ParseConfig(frontMatter);

        return new WorkflowDefinition(
            config,
            body.Trim(),
            fullPath,
            DateTimeOffset.UtcNow);
    }

    private static (string? FrontMatter, string Body) SplitFrontMatter(string content)
    {
        using var reader = new StringReader(content);
        var firstLine = reader.ReadLine();

        if (firstLine is null)
        {
            return (null, string.Empty);
        }

        if (firstLine.Trim() != "---")
        {
            return (null, content);
        }

        var yaml = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Trim() == "---")
            {
                return (string.Join(Environment.NewLine, yaml), reader.ReadToEnd());
            }

            yaml.Add(line);
        }

        throw new WorkflowLoadException("Failed to parse WORKFLOW.md: front matter was not closed.", "workflow_parse_error");
    }

    private IReadOnlyDictionary<string, object?> ParseConfig(string? frontMatter)
    {
        if (string.IsNullOrWhiteSpace(frontMatter))
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        object? parsed;
        try
        {
            parsed = _deserializer.Deserialize<object>(frontMatter);
        }
        catch (YamlException ex)
        {
            throw new WorkflowLoadException($"Failed to parse WORKFLOW.md: {ex.Message}", "workflow_parse_error", ex);
        }

        if (parsed is null)
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        if (NormalizeYamlValue(parsed) is not IReadOnlyDictionary<string, object?> map)
        {
            throw new WorkflowLoadException(
                "Failed to parse WORKFLOW.md: workflow front matter must decode to a map.",
                "workflow_front_matter_not_a_map");
        }

        return map;
    }

    private static object? NormalizeYamlValue(object? value)
    {
        return value switch
        {
            IDictionary<object, object?> dictionary => dictionary.ToDictionary(
                pair => Convert.ToString(pair.Key) ?? string.Empty,
                pair => NormalizeYamlValue(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            IEnumerable<object?> list when value is not string => list.Select(NormalizeYamlValue).ToArray(),
            _ => value
        };
    }
}

public sealed class WorkflowLoadException : InvalidOperationException
{
    public WorkflowLoadException(string message, string errorCode = "workflow_parse_error", Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
