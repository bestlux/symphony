using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Symphony.Codex;

public sealed class CodexProcess : IAsyncDisposable
{
    private readonly Process _process;

    private CodexProcess(Process process)
    {
        _process = process;
    }

    public int? ProcessId => _process.HasExited ? null : _process.Id;
    public StreamWriter StandardInput => _process.StandardInput;
    public StreamReader StandardOutput => _process.StandardOutput;
    public StreamReader StandardError => _process.StandardError;
    public Task WaitForExitAsync(CancellationToken cancellationToken) => _process.WaitForExitAsync(cancellationToken);

    public static CodexProcess Start(string workspacePath, string command, string? workerHost = null)
    {
        var launch = workerHost is { Length: > 0 }
            ? RemoteShell(workerHost, $"cd {ShellQuote(workspacePath)} && exec {command}")
            : LocalShell(command);

        var startInfo = new ProcessStartInfo
        {
            FileName = launch.FileName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in launch.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (workerHost is null)
        {
            startInfo.WorkingDirectory = workspacePath;
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Codex app-server process.");
        return new CodexProcess(process);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }

        _process.Dispose();
        await ValueTask.CompletedTask;
    }

    private static (string FileName, string[] Arguments) LocalShell(string command)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ("powershell", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command]);
        }

        return ("bash", ["-lc", command]);
    }

    private static (string FileName, string[] Arguments) RemoteShell(string workerHost, string command)
    {
        var host = workerHost;
        string[] portArgs = [];
        var colon = workerHost.LastIndexOf(':');

        if (colon > 0 && int.TryParse(workerHost[(colon + 1)..], out var port))
        {
            host = workerHost[..colon];
            portArgs = ["-p", port.ToString()];
        }

        var args = new List<string> { "-T" };
        var sshConfig = Environment.GetEnvironmentVariable("SYMPHONY_SSH_CONFIG");
        if (!string.IsNullOrWhiteSpace(sshConfig))
        {
            args.AddRange(["-F", sshConfig]);
        }

        args.AddRange(portArgs);
        args.Add(host);
        args.Add($"bash -lc {ShellQuote(command)}");
        return ("ssh", [.. args]);
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
}
