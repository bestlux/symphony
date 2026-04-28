namespace Symphony.Core.Workflow;

public sealed class WorkflowStore
{
    private readonly WorkflowLoader _loader;
    private readonly Action<string, Exception?>? _logWarning;
    private readonly object _gate = new();
    private WorkflowDefinition? _current;
    private DateTime _lastWriteTimeUtc;

    public WorkflowStore(string path, WorkflowLoader? loader = null, Action<string, Exception?>? logWarning = null)
    {
        Path = System.IO.Path.GetFullPath(path);
        _loader = loader ?? new WorkflowLoader();
        _logWarning = logWarning;
    }

    public string Path { get; }

    public WorkflowDefinition Current
    {
        get
        {
            lock (_gate)
            {
                return _current ?? ReloadRequired();
            }
        }
    }

    public WorkflowDefinition ReloadIfChanged()
    {
        lock (_gate)
        {
            if (_current is null)
            {
                return ReloadRequired();
            }

            var currentWriteTime = File.GetLastWriteTimeUtc(Path);
            if (currentWriteTime <= _lastWriteTimeUtc)
            {
                return _current;
            }

            try
            {
                return ReloadRequired();
            }
            catch (Exception ex)
            {
                _logWarning?.Invoke($"Invalid workflow reload retained previous good config: {ex.Message}", ex);
                return _current;
            }
        }
    }

    private WorkflowDefinition ReloadRequired()
    {
        var loaded = _loader.Load(Path);
        _current = loaded;
        _lastWriteTimeUtc = File.GetLastWriteTimeUtc(Path);
        return loaded;
    }
}
