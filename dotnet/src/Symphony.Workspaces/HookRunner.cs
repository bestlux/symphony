using System.Diagnostics;

namespace Symphony.Workspaces;

public sealed class HookRunner
{
    public async Task RunLocalAsync(string hookName, string? command, string workspace, int timeoutMs, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        var (fileName, arguments) = SelectLocalShell(command);
        var result = await RunProcessAsync(fileName, arguments, workspace, timeoutMs, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new WorkspaceException($"Workspace hook '{hookName}' failed with exit code {result.ExitCode}. Output={Summarize(result.Output)}");
        }
    }

    public async Task RunRemoteAsync(SshClient sshClient, string workerHost, string hookName, string? command, string workspace, int timeoutMs, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        var result = await sshClient.RunAsync(workerHost, $"cd {SshClient.ShellEscape(workspace)} && {command}", timeoutMs, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new WorkspaceException($"Remote workspace hook '{hookName}' failed on '{workerHost}' with exit code {result.ExitCode}. Output={Summarize(result.Output)}");
        }
    }

    internal static async Task<CommandResult> RunProcessAsync(
        string fileName,
        string arguments,
        string? workingDirectory,
        int timeoutMs,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(timeoutMs, 1)));

        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false) + await errorTask.ConfigureAwait(false);
            return new CommandResult(process.ExitCode, output);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new WorkspaceException($"Command timed out after {timeoutMs} ms: {fileName} {arguments}");
        }
        catch (Exception ex) when (ex is not WorkspaceException)
        {
            throw new WorkspaceException($"Command failed to start: {fileName} {arguments}", ex);
        }
    }

    private static (string FileName, string Arguments) SelectLocalShell(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            var bash = FindOnPath("bash");
            if (bash is not null && LooksPosixOriented(command))
            {
                return (bash, "-lc " + QuoteForArgument(command));
            }

            return ("powershell", "-NoProfile -ExecutionPolicy Bypass -Command " + QuoteForArgument(command));
        }

        return ("sh", "-lc " + QuoteForArgument(command));
    }

    private static bool LooksPosixOriented(string command)
    {
        return command.Contains("#!/usr/bin/env bash", StringComparison.Ordinal)
            || command.Contains("#!/bin/bash", StringComparison.Ordinal)
            || command.Contains("set -e", StringComparison.Ordinal)
            || command.Contains("&&", StringComparison.Ordinal)
            || command.Contains("chmod ", StringComparison.Ordinal)
            || command.Contains("./", StringComparison.Ordinal);
    }

    private static string? FindOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var candidates = OperatingSystem.IsWindows()
            ? new[] { executable, executable + ".exe", executable + ".cmd", executable + ".bat" }
            : [executable];

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var candidate in candidates)
            {
                var fullPath = Path.Combine(directory, candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }

    private static string QuoteForArgument(string value)
    {
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string Summarize(string output)
    {
        var normalized = string.Join(' ', output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 2_048 ? normalized : normalized[..2_048] + "... (truncated)";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup after timeout.
        }
    }
}

public sealed record CommandResult(int ExitCode, string Output);

