namespace Symphony.Workspaces;

public sealed class SshClient
{
    public async Task<CommandResult> RunAsync(string workerHost, string command, int timeoutMs, CancellationToken cancellationToken = default)
    {
        var executable = FindSsh() ?? throw new WorkspaceException("ssh executable was not found on PATH.");
        var args = BuildArguments(workerHost, command);
        return await HookRunner.RunProcessAsync(executable, args, workingDirectory: null, timeoutMs, cancellationToken).ConfigureAwait(false);
    }

    public static string RemoteShellCommand(string command)
    {
        return "bash -lc " + ShellEscape(command);
    }

    public static string ShellEscape(string value)
    {
        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }

    private static string BuildArguments(string workerHost, string command)
    {
        var target = ParseTarget(workerHost);
        var parts = new List<string>();

        var configPath = Environment.GetEnvironmentVariable("SYMPHONY_SSH_CONFIG");
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            parts.Add("-F");
            parts.Add(Quote(configPath));
        }

        parts.Add("-T");
        if (target.Port is not null)
        {
            parts.Add("-p");
            parts.Add(target.Port);
        }

        parts.Add(Quote(target.Destination));
        parts.Add(Quote(RemoteShellCommand(command)));
        return string.Join(' ', parts);
    }

    private static SshTarget ParseTarget(string workerHost)
    {
        var trimmed = workerHost.Trim();
        var lastColon = trimmed.LastIndexOf(':');
        if (lastColon > 0 && lastColon < trimmed.Length - 1)
        {
            var destination = trimmed[..lastColon];
            var port = trimmed[(lastColon + 1)..];
            if (int.TryParse(port, out var parsedPort) && parsedPort is > 0 and <= 65535 && IsValidPortDestination(destination))
            {
                return new SshTarget(destination, port);
            }
        }

        return new SshTarget(trimmed, null);
    }

    private static bool IsValidPortDestination(string destination)
    {
        return destination.Length > 0 && (!destination.Contains(':', StringComparison.Ordinal) || IsBracketedHost(destination));
    }

    private static bool IsBracketedHost(string destination)
    {
        return destination.Contains('[', StringComparison.Ordinal) && destination.Contains(']', StringComparison.Ordinal);
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string? FindSsh()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var fullPath = Path.Combine(directory, OperatingSystem.IsWindows() ? "ssh.exe" : "ssh");
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }

    private sealed record SshTarget(string Destination, string? Port);
}

